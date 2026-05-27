using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.addons.mega_text;
using LocalMultiControl.Scripts.Runtime;

namespace LocalMultiControl.Scripts.Patch;

/* Fast path: NEnergyCounter.OnEnergyChanged in the base game only plays VFX
 * on energy gain. Force a RefreshLabel after every energy change so the
 * display updates immediately in the common case. */
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

/* Safety net: per-frame check that the displayed label matches the model.
 * Catches any case where OnEnergyChanged was missed (replaced/orphaned
 * counter, non-standard energy mutation, subscription race during card play).
 * Cheap because the string compare short-circuits whenever they agree. */
[HarmonyPatch(typeof(NEnergyCounter), "_Process")]
internal static class NEnergyCounterProcessRefreshPatch
{
    [HarmonyPostfix]
    private static void Postfix(NEnergyCounter __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        Player? player = AccessTools.Field(typeof(NEnergyCounter), "_player")?.GetValue(__instance) as Player;
        PlayerCombatState? state = player?.PlayerCombatState;
        if (state == null)
        {
            return;
        }

        MegaLabel? label = AccessTools.Field(typeof(NEnergyCounter), "_label")?.GetValue(__instance) as MegaLabel;
        if (label == null || !GodotObject.IsInstanceValid(label))
        {
            return;
        }

        string expected = $"{state.Energy}/{state.MaxEnergy}";
        if (label.Text == expected)
        {
            return;
        }

        AccessTools.Method(typeof(NEnergyCounter), "RefreshLabel")?.Invoke(__instance, Array.Empty<object>());
    }
}
