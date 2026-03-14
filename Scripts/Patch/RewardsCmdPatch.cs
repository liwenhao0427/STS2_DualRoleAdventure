using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RewardsCmd), nameof(RewardsCmd.OfferForRoomEnd))]
internal static class RewardsCmdPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player player, AbstractRoom room, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || room is not CombatRoom combatRoom || player.RunState.Players.Count <= 1)
        {
            return true;
        }

        __result = OfferSharedRewardsForControlledPlayer(combatRoom);
        return false;
    }

    private static async Task OfferSharedRewardsForControlledPlayer(CombatRoom combatRoom)
    {
        IRunState runState = combatRoom.CombatState.RunState;
        Player? rewardPlayer = LocalContext.GetMe(runState);
        if (rewardPlayer == null)
        {
            return;
        }

        RewardsSet rewardsSet = combatRoom.Encounter != null && !combatRoom.Encounter.ShouldGiveRewards
            ? new RewardsSet(rewardPlayer).EmptyForRoom(combatRoom)
            : new RewardsSet(rewardPlayer).WithRewardsFromRoom(combatRoom);

        AppendCardRewardsForOtherPlayers(combatRoom, rewardsSet, rewardPlayer);
        await rewardsSet.Offer();
    }

    private static void AppendCardRewardsForOtherPlayers(CombatRoom combatRoom, RewardsSet rewardsSet, Player currentPlayer)
    {
        List<Player> otherPlayers = currentPlayer.RunState.Players
            .Where((player) => player.NetId != currentPlayer.NetId)
            .ToList();
        if (otherPlayers.Count == 0)
        {
            return;
        }

        List<CardReward> primaryCardRewards = rewardsSet.Rewards
            .OfType<CardReward>()
            .Where((reward) => reward.Player.NetId == currentPlayer.NetId)
            .ToList();
        if (primaryCardRewards.Count == 0)
        {
            return;
        }

        int extraRewardCount = 0;
        foreach (CardReward primaryCardReward in primaryCardRewards)
        {
            int optionCount = AccessTools.Property(typeof(CardReward), "OptionCount")?.GetValue(primaryCardReward) as int? ?? 3;
            int insertOffset = 1;
            foreach (Player otherPlayer in otherPlayers)
            {
                CardReward extraCardReward = new(CardCreationOptions.ForRoom(otherPlayer, combatRoom.RoomType), optionCount, otherPlayer)
                {
                    CanReroll = primaryCardReward.CanReroll
                };

                int insertIndex = rewardsSet.Rewards.IndexOf(primaryCardReward);
                if (insertIndex >= 0)
                {
                    rewardsSet.Rewards.Insert(insertIndex + insertOffset, extraCardReward);
                }
                else
                {
                    rewardsSet.Rewards.Add(extraCardReward);
                }

                insertOffset++;
                extraRewardCount++;
            }
        }

        LocalMultiControlLogger.Info(
            $"战后奖励已扩展额外卡池: owner={currentPlayer.NetId}, extraPlayers={otherPlayers.Count}, extraRewards={extraRewardCount}");
    }
}
