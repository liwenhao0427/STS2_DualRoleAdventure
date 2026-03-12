using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NRemoteMouseCursorContainer), nameof(NRemoteMouseCursorContainer.GetCursorPosition))]
internal static class NRemoteMouseCursorContainerPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NRemoteMouseCursorContainer __instance, ulong playerId, ref Vector2 __result)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return true;
        }

        try
        {
            object? cursor = AccessTools.Method(typeof(NRemoteMouseCursorContainer), "GetCursor")?.Invoke(__instance, new object[] { playerId });
            if (cursor == null)
            {
                __result = Vector2.Zero;
                return false;
            }
        }
        catch
        {
            __result = Vector2.Zero;
            return false;
        }

        return true;
    }
}
