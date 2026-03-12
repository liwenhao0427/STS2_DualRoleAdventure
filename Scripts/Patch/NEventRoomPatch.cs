using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NEventRoom), "RefreshEventState")]
internal static class NEventRoomPatch
{
    [HarmonyPostfix]
    private static void Postfix(EventModel eventModel)
    {
        if (!LocalSelfCoopContext.ShouldQueueEventAutoSwitchAfterEventState(eventModel))
        {
            return;
        }

        if (NOverlayStack.Instance?.ScreenCount > 0)
        {
            LocalMultiControlLogger.Info("事件流程已完成，等待奖励/选择弹窗关闭后自动切换角色。");
            return;
        }

        Callable.From(delegate
        {
            if (RunManager.Instance.IsInProgress)
            {
                LocalMultiControlRuntime.TryRunPendingEventAutoSwitch("event-auto-next");
            }
        }).CallDeferred();
    }
}
