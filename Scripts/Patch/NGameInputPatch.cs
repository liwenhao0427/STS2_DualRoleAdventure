using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
internal static class NGameInputPatch
{
    [HarmonyPostfix]
    private static void Postfix(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey keyEvent || !keyEvent.IsReleased())
        {
            return;
        }

        if (keyEvent.Keycode == Key.Bracketleft)
        {
            LocalMultiControlRuntime.SwitchPreviousControlledPlayer("hotkey:[");
        }
        else if (keyEvent.Keycode == Key.Bracketright)
        {
            LocalMultiControlRuntime.SwitchNextControlledPlayer("hotkey:]");
        }
    }
}
