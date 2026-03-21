using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Relics;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(Toolbox), nameof(Toolbox.BeforeHandDraw))]
internal static class ToolboxPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        Toolbox __instance,
        Player player,
        PlayerChoiceContext choiceContext,
        CombatState combatState,
        ref Task __result)
    {
        if (!ShouldAutoPickFirstCard(__instance, player))
        {
            return true;
        }

        __result = AutoPickFirstCardAsync(__instance, player, choiceContext);
        return false;
    }

    private static bool ShouldAutoPickFirstCard(Toolbox relic, Player player)
    {
        if (!LocalSelfCoopContext.IsEnabled || player != relic.Owner)
        {
            return false;
        }

        if (!LocalSelfCoopContext.IsWakuuEnabled(player.NetId))
        {
            return false;
        }

        if (player.GetRelic<WhisperingEarring>() == null)
        {
            return false;
        }

        CombatState? combatState = player.Creature.CombatState;
        return combatState != null && combatState.RoundNumber == 1;
    }

    private static async Task AutoPickFirstCardAsync(
        Toolbox relic,
        Player player,
        PlayerChoiceContext choiceContext)
    {
        if (player != relic.Owner || player.Creature.CombatState == null || player.Creature.CombatState.RoundNumber != 1)
        {
            return;
        }

        relic.Flash();
        List<CardModel> cards = CardFactory.GetDistinctForCombat(
                relic.Owner,
                ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint),
                relic.DynamicVars.Cards.IntValue,
                relic.Owner.RunState.Rng.CombatCardGeneration)
            .ToList();

        CardModel? pickedCard;
        using (CardSelectCmd.PushSelector(new VakuuCardSelector()))
        {
            pickedCard = await CardSelectCmd.FromChooseACardScreen(choiceContext, cards, relic.Owner);
        }

        if (pickedCard != null)
        {
            await CardPileCmd.AddGeneratedCardToCombat(pickedCard, PileType.Hand, addedByPlayer: true);
            LocalMultiControlLogger.Info($"瓦库工具箱已自动选择首张卡: player={player.NetId}, card={pickedCard.Id.Entry}");
        }
    }
}
