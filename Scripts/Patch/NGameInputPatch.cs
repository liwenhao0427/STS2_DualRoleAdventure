using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

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
            if (RunManager.Instance.IsInProgress)
            {
                LocalMultiControlRuntime.SwitchPreviousControlledPlayer("hotkey:[");
            }
            else
            {
                LocalSelfCoopContext.SwitchLobbyEditingPlayer(next: false);
            }
        }
        else if (keycode == Key.Bracketright ||
                 physicalKeycode == Key.Bracketright ||
                 keycode == Key.Slash ||
                 physicalKeycode == Key.Slash ||
                 keycode == Key.R ||
                 physicalKeycode == Key.R)
        {
            LocalMultiControlLogger.Info("检测到切换热键: ]/R");
            if (RunManager.Instance.IsInProgress)
            {
                LocalMultiControlRuntime.SwitchNextControlledPlayer("hotkey:]/R");
            }
            else
            {
                LocalSelfCoopContext.SwitchLobbyEditingPlayer(next: true);
            }
        }
    }
}
