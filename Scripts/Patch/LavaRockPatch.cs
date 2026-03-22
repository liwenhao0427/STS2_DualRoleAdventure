using System.Collections.Generic;
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

        if (room == null || room.RoomType != RoomType.Boss)
        {
            __result = false;
            return false;
        }

        if (__instance.Owner.RunState.CurrentActIndex != 0 || __instance.HasTriggered)
        {
            __result = false;
            return false;
        }

        __instance.Flash();
        int extraRelicCount = __instance.DynamicVars["Relics"].IntValue;
        for (int i = 0; i < extraRelicCount; i++)
        {
            rewards.Add(new RelicReward(player));
        }

        __instance.HasTriggered = true;
        __instance.Status = RelicStatus.Disabled;
        __result = true;
        LocalMultiControlLogger.Info(
            $"本地多控已修正熔岩石触发归属: owner={__instance.Owner.NetId}, rewardPlayer={player.NetId}, added={extraRelicCount}");
        return false;
    }
}
