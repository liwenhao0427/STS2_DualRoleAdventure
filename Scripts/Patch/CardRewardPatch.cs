using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(CardReward), "Populate")]
internal static class CardRewardPatchPopulate
{
    [HarmonyPostfix]
    private static void Postfix(CardReward __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        try
        {
            Player currentPlayer = __instance.Player;
            if (currentPlayer == null || currentPlayer.RunState == null)
            {
                return;
            }

            IRunState runState = currentPlayer.RunState;
            IEnumerable<Player> otherPlayers = runState.Players.Where(p => p.NetId != currentPlayer.NetId);
            Player? otherPlayer = otherPlayers.FirstOrDefault();
            if (otherPlayer == null)
            {
                return;
            }

            CardCreationOptions options = AccessTools.Property(typeof(CardReward), "Options")?.GetValue(__instance) as CardCreationOptions;
            if (options == null)
            {
                return;
            }

            if (options.CustomCardPool != null && options.CustomCardPool.Count() > 0)
            {
                return;
            }

            if (options.CardPools == null || options.CardPools.Count == 0)
            {
                return;
            }

            AbstractRoom? room = runState.CurrentRoom;
            if (room is not CombatRoom combatRoom)
            {
                return;
            }

            CardReward extraReward = new CardReward(options, 3, otherPlayer);
            combatRoom.AddExtraReward(otherPlayer, extraReward);

            LocalMultiControlLogger.Info($"卡牌奖励已添加额外组: currentPlayer={currentPlayer.NetId}, otherPlayer={otherPlayer.NetId}");
        }
        catch (Exception ex)
        {
            LocalMultiControlLogger.Warn($"添加额外卡牌奖励组失败: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(CardReward), "OnSelect")]
internal static class CardRewardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardReward __instance, ref Task<bool> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        __result = SelectForCurrentControlledPlayer(__instance);
        return false;
    }

    private static async Task<bool> SelectForCurrentControlledPlayer(CardReward reward)
    {
        LocalMultiControlLogger.Info("卡牌奖励已切换为当前控制角色归属模式。");

        bool removeReward = false;
        List<CardModel> chosenCards = new List<CardModel>();
        List<CardCreationResult>? cards = AccessTools.Field(typeof(CardReward), "_cards")?.GetValue(reward) as List<CardCreationResult>;
        if (cards == null)
        {
            throw new InvalidOperationException("无法读取卡牌奖励选项列表。");
        }
        IReadOnlyList<CardRewardAlternative> rewardOptions = CardRewardAlternative.Generate(reward);
        NCardRewardSelectionScreen? selectionScreen = NCardRewardSelectionScreen.ShowScreen(cards, rewardOptions);
        AccessTools.Field(typeof(CardReward), "_currentlyShownScreen")?.SetValue(reward, selectionScreen);
        Action<RelicModel> onRelicObtainedHandler =
            (Action<RelicModel>)AccessTools.Method(typeof(CardReward), "OnRelicObtained")!
                .CreateDelegate(typeof(Action<RelicModel>), reward);

        while (true)
        {
            CardModel? selectedCardTemplate = null;
            NCardHolder? selectedHolder = null;
            if (selectionScreen != null)
            {
                Tuple<IEnumerable<NCardHolder>, bool> result = await selectionScreen.CardsSelected();
                removeReward = result.Item2;
                selectedHolder = result.Item1.FirstOrDefault();
                selectedCardTemplate = selectedHolder?.CardNode?.Model;
            }
            else
            {
                selectedCardTemplate = CardSelectCmd.Selector?.GetSelectedCardReward(cards, rewardOptions);
            }

            if (selectedCardTemplate == null && !removeReward)
            {
                continue;
            }

            if (selectedCardTemplate != null)
            {
                Player receiver = ResolveCurrentControlledPlayer(reward.Player);
                CardModel cardToAdd = selectedCardTemplate;
                if (selectedCardTemplate.Owner != receiver)
                {
                    cardToAdd = CardModel.FromSerializable(selectedCardTemplate.ToSerializable());
                    receiver.RunState.AddCard(cardToAdd, receiver);
                }

                CardPileAddResult addResult = await CardPileCmd.Add(cardToAdd, PileType.Deck);
                if (addResult.success)
                {
                    CardModel addedCard = addResult.cardAdded;
                    chosenCards.Add(addedCard);
                    cards.RemoveAll((card) => card.Card == selectedCardTemplate);

                    if (selectedHolder != null)
                    {
                        NCard? cardNode = selectedHolder.CardNode;
                        if (cardNode != null)
                        {
                            NRun? runNode = NRun.Instance;
                            if (runNode != null)
                            {
                                runNode.GlobalUi.ReparentCard(cardNode);
                                selectedHolder.QueueFreeSafely();
                                Godot.Vector2 targetPosition = PileType.Deck.GetTargetPosition(cardNode);
                                runNode.GlobalUi.TopBar.TrailContainer.AddChildSafely(
                                    NCardFlyVfx.Create(cardNode, targetPosition, isAddingToPile: true, addedCard.Owner.Character.TrailPath));
                            }
                        }
                    }

                    Log.Info($"Obtained {addedCard.Id} from card reward for player {receiver.NetId}");
                    RunManager.Instance.RewardSynchronizer.SyncLocalObtainedCard(addedCard);
                }
            }

            if (selectedCardTemplate == null || !MegaCrit.Sts2.Core.Hooks.Hook.ShouldAllowSelectingMoreCardRewards(reward.Player.RunState, reward.Player, reward))
            {
                break;
            }
        }

        reward.Player.RelicObtained -= onRelicObtainedHandler;

        Player finalReceiver = ResolveCurrentControlledPlayer(reward.Player);
        foreach (CardModel card in chosenCards)
        {
            finalReceiver.RunState.CurrentMapPointHistoryEntry?.GetEntry(finalReceiver.NetId).CardChoices.Add(new CardChoiceHistoryEntry(card, wasPicked: true));
        }

        if (removeReward)
        {
            foreach (CardCreationResult card in cards)
            {
                finalReceiver.RunState.CurrentMapPointHistoryEntry?.GetEntry(finalReceiver.NetId).CardChoices.Add(new CardChoiceHistoryEntry(card.Card, wasPicked: false));
                RunManager.Instance.RewardSynchronizer.SyncLocalSkippedCard(card.Card);
            }
        }

        if (selectionScreen != null)
        {
            NOverlayStack.Instance?.Remove(selectionScreen);
            AccessTools.Field(typeof(CardReward), "_currentlyShownScreen")?.SetValue(reward, null);
        }

        return removeReward;
    }

    private static Player ResolveCurrentControlledPlayer(Player fallback)
    {
        ulong currentPlayerId = LocalContext.NetId ?? fallback.NetId;
        return fallback.RunState.GetPlayer(currentPlayerId) ?? fallback;
    }
}
