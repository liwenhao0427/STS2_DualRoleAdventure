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

        bool isPrevious = keycode == Key.Bracketleft ||
                          physicalKeycode == Key.Bracketleft ||
                          keycode == Key.T ||
                          physicalKeycode == Key.T;

        bool isNext = keycode == Key.Bracketright ||
                      physicalKeycode == Key.Bracketright ||
                      keycode == Key.R ||
                      physicalKeycode == Key.R ||
                      keycode == Key.Slash ||
                      physicalKeycode == Key.Slash;

        if (!isPrevious && !isNext)
        {
            return;
        }

        if (isPrevious)
        {
            LocalMultiControlLogger.Info("检测到切换热键: [ / T (反切)");
            if (RunManager.Instance.IsInProgress)
            {
                LocalMultiControlRuntime.SwitchPreviousControlledPlayer("hotkey:[/T");
            }
            else
            {
                LocalSelfCoopContext.SwitchLobbyEditingPlayer(next: false);
            }

            return;
        }

        LocalMultiControlLogger.Info("检测到切换热键: ] / R (正切)");
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
