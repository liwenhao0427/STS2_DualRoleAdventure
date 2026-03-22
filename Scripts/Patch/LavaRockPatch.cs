using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(LavaRock), nameof(LavaRock.TryModifyRewards))]
internal static class LavaRockPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        LavaRock __instance,
        Player player,
        List<Reward> rewards,
        AbstractRoom? room,
        ref bool __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (player.RunState == null)
        {
            __result = false;
            return false;
        }

        if (room == null || room.RoomType != RoomType.Boss)
        {
            __result = false;
            return false;
        }

        if (player.RunState.CurrentActIndex != 0)
        {
            __result = false;
            return false;
        }

        List<LavaRock> pendingLavaRocks = player.RunState.Players
            .Select((member) => member.GetRelic<LavaRock>())
            .Where((relic) => relic != null && !relic.HasTriggered)
            .Cast<LavaRock>()
            .ToList();
        if (pendingLavaRocks.Count == 0)
        {
            __result = false;
            return false;
        }

        int extraRelicCount = pendingLavaRocks[0].DynamicVars["Relics"].IntValue;
        for (int i = 0; i < extraRelicCount; i++)
        {
            rewards.Add(new RelicReward(player));
        }

        foreach (LavaRock lavaRock in pendingLavaRocks)
        {
            lavaRock.Flash();
            lavaRock.HasTriggered = true;
            lavaRock.Status = RelicStatus.Disabled;
        }

        __result = true;
        string owners = string.Join(",", pendingLavaRocks.Select((relic) => relic.Owner.NetId));
        LocalMultiControlLogger.Info(
            $"本地多控已按队伍熔岩石触发首领额外遗物: rewardPlayer={player.NetId}, owners={owners}, added={extraRelicCount}");
        return false;
    }
}
