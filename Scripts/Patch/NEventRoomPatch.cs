using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Events;
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

[HarmonyPatch(typeof(NEventRoom), nameof(NEventRoom.OptionButtonClicked))]
internal static class NEventRoomOptionButtonPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NEventRoom __instance, EventOption option)
    {
        if (!LocalSelfCoopContext.IsEnabled || !option.IsProceed || !RunManager.Instance.IsInProgress)
        {
            return true;
        }

        if (RunManager.Instance.EventSynchronizer.IsShared)
        {
            return true;
        }

        EventModel? currentEvent = AccessTools.Field(typeof(NEventRoom), "_event")?.GetValue(__instance) as EventModel;
        if (currentEvent?.Owner == null || !currentEvent.IsFinished)
        {
            return true;
        }

        EventModel? pendingEvent = RunManager.Instance.EventSynchronizer.Events.FirstOrDefault((eventModel) =>
            eventModel.Owner != null &&
            eventModel.Owner.NetId != currentEvent.Owner.NetId &&
            !eventModel.IsFinished);
        if (pendingEvent?.Owner == null)
        {
            return true;
        }

        LocalMultiControlLogger.Info($"检测到另一名角色尚未完成事件，拦截 Proceed 并切换到 player={pendingEvent.Owner.NetId}");
        LocalMultiControlRuntime.SwitchControlledPlayerTo(pendingEvent.Owner.NetId, "event-proceed-next-player");
        return false;
    }
}
