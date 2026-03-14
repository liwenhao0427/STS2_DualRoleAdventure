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
        if (!LocalSelfCoopContext.IsEnabled || room is not CombatRoom combatRoom || player.RunState.Players.Count != 2)
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

        RewardsSet rewardsSet;
        if (combatRoom.Encounter != null && !combatRoom.Encounter.ShouldGiveRewards)
        {
            rewardsSet = new RewardsSet(rewardPlayer).EmptyForRoom(combatRoom);
        }
        else
        {
            rewardsSet = new RewardsSet(rewardPlayer).WithRewardsFromRoom(combatRoom);
        }

        AppendSecondCardRewardForOtherPlayer(combatRoom, rewardsSet, rewardPlayer);
        await rewardsSet.Offer();
    }

    private static void AppendSecondCardRewardForOtherPlayer(CombatRoom combatRoom, RewardsSet rewardsSet, Player currentPlayer)
    {
        Player? otherPlayer = currentPlayer.RunState.Players.FirstOrDefault((player) => player.NetId != currentPlayer.NetId);
        if (otherPlayer == null)
        {
            return;
        }

        List<CardReward> primaryCardRewards = rewardsSet.Rewards
            .OfType<CardReward>()
            .Where((reward) => reward.Player.NetId == currentPlayer.NetId)
            .ToList();
        foreach (CardReward primaryCardReward in primaryCardRewards)
        {
            int optionCount = AccessTools.Property(typeof(CardReward), "OptionCount")?.GetValue(primaryCardReward) as int? ?? 3;
            CardReward secondaryCardReward = new CardReward(CardCreationOptions.ForRoom(otherPlayer, combatRoom.RoomType), optionCount, otherPlayer)
            {
                CanReroll = primaryCardReward.CanReroll
            };

            int insertIndex = rewardsSet.Rewards.IndexOf(primaryCardReward);
            if (insertIndex >= 0)
            {
                rewardsSet.Rewards.Insert(insertIndex + 1, secondaryCardReward);
            }
            else
            {
                rewardsSet.Rewards.Add(secondaryCardReward);
            }
        }

        if (primaryCardRewards.Count > 0)
        {
            LocalMultiControlLogger.Info($"战后奖励已扩展为双卡池条目: playerA={currentPlayer.NetId}, playerB={otherPlayer.NetId}, groups={primaryCardRewards.Count * 2}");
        }
    }
}
