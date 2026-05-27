using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using LocalMultiControl.Scripts.Runtime;

namespace LocalMultiControl.Scripts.Patch;

/* Energy desync fix: NEnergyCounter.OnEnergyChanged in the base game only plays
 * VFX on energy gain. Label updates rely on a deferred CombatStateChanged chain
 * that can lag/miss in local multi-control. Force a RefreshLabel after every
 * energy change so the display stays in sync with the model. */
[HarmonyPatch(typeof(NEnergyCounter), "OnEnergyChanged")]
internal static class NEnergyCounterRefreshOnEnergyChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(NEnergyCounter __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        AccessTools.Method(typeof(NEnergyCounter), "RefreshLabel")?.Invoke(__instance, Array.Empty<object>());
    }
}
