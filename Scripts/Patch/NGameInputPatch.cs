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

        Key keycode = keyEvent.Keycode;
        Key physicalKeycode = keyEvent.PhysicalKeycode;
        if (keycode == Key.Bracketleft || physicalKeycode == Key.Bracketleft)
        {
            LocalMultiControlLogger.Info("检测到切换热键: [");
            LocalMultiControlRuntime.SwitchPreviousControlledPlayer("hotkey:[");
        }
        else if (keycode == Key.Bracketright || physicalKeycode == Key.Bracketright)
        {
            LocalMultiControlLogger.Info("检测到切换热键: ]");
            LocalMultiControlRuntime.SwitchNextControlledPlayer("hotkey:]");
        }
    }
}
