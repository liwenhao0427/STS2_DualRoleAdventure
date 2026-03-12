using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;

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

        __result = OfferForAllLocalPlayers(combatRoom);
        return false;
    }

    private static async Task OfferForAllLocalPlayers(CombatRoom combatRoom)
    {
        foreach (Player rewardPlayer in combatRoom.CombatState.RunState.Players)
        {
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
}
