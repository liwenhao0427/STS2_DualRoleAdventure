using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Combat;
using LocalMultiControl.Scripts.Runtime;

namespace LocalMultiControl.Scripts.Patch;

/* Under-creature health bar fix: NCreature._Ready marks any player that isn't
 * the local "me" as a remote player (_isRemotePlayerOrPet) and calls
 * _stateDisplay.HideImmediately(), so the health bar beneath a non-active hero
 * stays hidden until you hover them. In local multi-control every hero is local,
 * so clear the remote flag and reveal the bar — making all heroes' health bars
 * permanently visible and giving them normal (local) hover behavior.
 *
 * Gated to local multi-control: in real online multiplayer the remote-player
 * flag drives focus/highlight logic we don't want to alter. */
[HarmonyPatch(typeof(NCreature), "_Ready")]
internal static class NCreatureHealthBarVisibilityPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCreature __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        if (__instance.Entity is not { IsPlayer: true })
        {
            return;
        }

        bool isRemote = AccessTools.Field(typeof(NCreature), "_isRemotePlayerOrPet")?.GetValue(__instance) as bool? ?? false;
        if (!isRemote)
        {
            return;
        }

        AccessTools.Field(typeof(NCreature), "_isRemotePlayerOrPet")?.SetValue(__instance, false);

        if (AccessTools.Field(typeof(NCreature), "_stateDisplay")?.GetValue(__instance) is NCreatureStateDisplay stateDisplay)
        {
            stateDisplay.AnimateIn(HealthBarAnimMode.SpawnedAtCombatStart);
        }
    }
}
