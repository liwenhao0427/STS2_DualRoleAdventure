using System;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), new[] { typeof(RelicModel), typeof(Player), typeof(int) })]
internal static class RelicCmdObtainPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        GoldMirrorSuppressionContext.EnterSuppression();
    }

    [HarmonyPostfix]
    private static void Postfix(ref Task<RelicModel> __result)
    {
        __result = GoldMirrorSuppressionContext.ExitSuppressionWhenCompleteAsync(KeepRelicIndependentAsync(__result));
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        if (__exception != null)
        {
            GoldMirrorSuppressionContext.ExitSuppressionOnce();
        }

        return __exception;
    }

    private static async Task<RelicModel> KeepRelicIndependentAsync(Task<RelicModel> originalTask)
    {
        RelicModel relic = await originalTask;
        if (LocalSelfCoopContext.IsEnabled && LocalSelfCoopContext.UseSingleAdventureMode)
        {
            // 需求调整：遗物按角色独立结算，不做镜像复制。
        }

        return relic;
    }
}

[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Remove))]
internal static class RelicCmdRemovePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (LocalSelfCoopContext.IsEnabled && LocalSelfCoopContext.UseSingleAdventureMode)
        {
            // 需求调整：遗物移除按角色独立处理，不做镜像移除。
        }
    }
}
