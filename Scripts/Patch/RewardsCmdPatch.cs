using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;

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

        __result = OfferIndependentRewardsForAllPlayers(combatRoom);
        return false;
    }

    private static async Task OfferIndependentRewardsForAllPlayers(CombatRoom combatRoom)
    {
        List<Player> players = combatRoom.CombatState.RunState.Players.ToList();
        foreach (Player rewardPlayer in players)
        {
            if (rewardPlayer.Creature.IsDead)
            {
                continue;
            }

            LocalMultiControlRuntime.SwitchControlledPlayerTo(rewardPlayer.NetId, "rewards-independent-sequence");
            RewardsSet rewardsSet = combatRoom.Encounter != null && !combatRoom.Encounter.ShouldGiveRewards
                ? new RewardsSet(rewardPlayer).EmptyForRoom(combatRoom)
                : new RewardsSet(rewardPlayer).WithRewardsFromRoom(combatRoom);
            LocalMultiControlLogger.Info($"战后奖励独立结算: player={rewardPlayer.NetId}");
            await rewardsSet.Offer();
        }
    }
}
