using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using System.Linq;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NOverlayStack), nameof(NOverlayStack.Remove))]
internal static class NOverlayStackPatch
{
    [HarmonyPostfix]
    private static void Postfix(NOverlayStack __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || __instance.ScreenCount > 0)
        {
            return;
        }

        if (LocalSelfCoopContext.UseSingleEventFlow)
        {
            return;
        }

        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        Callable.From(delegate
        {
            if (TryAutoSwitchToNextPendingEventAfterOverlayClosed())
            {
                return;
            }

            LocalMultiControlRuntime.TryRunPendingEventAutoSwitch("event-auto-next");
        }).CallDeferred();
    }

    private static bool TryAutoSwitchToNextPendingEventAfterOverlayClosed()
    {
        if (RunManager.Instance.EventSynchronizer.IsShared)
        {
            return false;
        }

        NEventRoom? eventRoom = NEventRoom.Instance;
        if (eventRoom == null)
        {
            return false;
        }

        EventModel? currentEvent = AccessTools.Field(typeof(NEventRoom), "_event")?.GetValue(eventRoom) as EventModel;
        if (currentEvent?.Owner == null || !currentEvent.IsFinished)
        {
            return false;
        }

        EventModel? pendingEvent = RunManager.Instance.EventSynchronizer.Events.FirstOrDefault((eventModel) =>
            eventModel.Owner != null &&
            eventModel.Owner.NetId != currentEvent.Owner.NetId &&
            !eventModel.IsFinished);
        if (pendingEvent?.Owner == null)
        {
            return false;
        }

        LocalMultiControlLogger.Info(
            $"事件弹窗关闭后检测到待处理角色，自动切换: {currentEvent.Owner.NetId} -> {pendingEvent.Owner.NetId}");
        LocalMultiControlRuntime.SwitchControlledPlayerTo(pendingEvent.Owner.NetId, "event-overlay-closed-next-player");
        return true;
    }
}
