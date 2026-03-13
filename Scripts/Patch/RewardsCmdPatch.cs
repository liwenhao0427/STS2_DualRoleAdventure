using System.Threading.Tasks;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
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

        TryAppendSecondaryPoolCardReward(rewardsSet, rewardPlayer, runState, combatRoom);
        await rewardsSet.Offer();
    }

    private static void TryAppendSecondaryPoolCardReward(RewardsSet rewardsSet, Player rewardPlayer, IRunState runState, CombatRoom combatRoom)
    {
        if (!LocalMultiControlRuntime.HasDualAdventureStarterRelic(rewardPlayer))
        {
            return;
        }

        Player? otherPlayer = runState.Players.FirstOrDefault((candidate) => candidate.NetId != rewardPlayer.NetId);
        if (otherPlayer == null)
        {
            return;
        }

        CardReward extraCardReward = new CardReward(CardCreationOptions.ForRoom(otherPlayer, combatRoom.RoomType), 3, rewardPlayer);
        rewardsSet.WithCustomRewards(new System.Collections.Generic.List<Reward> { extraCardReward });
        LocalMultiControlLogger.Info($"起始遗物触发额外卡牌奖励池: from={otherPlayer.NetId}, to={rewardPlayer.NetId}");
    }
}
