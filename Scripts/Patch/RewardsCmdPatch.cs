using System.Threading.Tasks;
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

        await rewardsSet.Offer();
    }
}
