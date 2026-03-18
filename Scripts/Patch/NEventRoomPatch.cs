using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NEventRoom), "RefreshEventState")]
internal static class NEventRoomPatch
{
    [HarmonyPostfix]
    private static void Postfix(NEventRoom __instance, EventModel eventModel)
    {
        LocalEventSyncToggleUi.Refresh(__instance);

        if (LocalSelfCoopContext.UseSingleEventFlow)
        {
            return;
        }

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

[HarmonyPatch(typeof(NEventRoom), nameof(NEventRoom._Ready))]
internal static class NEventRoomReadyPatch
{
    [HarmonyPostfix]
    private static void Postfix(NEventRoom __instance)
    {
        LocalEventSyncToggleUi.Ensure(__instance);
    }
}

[HarmonyPatch(typeof(NEventRoom), nameof(NEventRoom._ExitTree))]
internal static class NEventRoomExitPatch
{
    [HarmonyPrefix]
    private static void Prefix(NEventRoom __instance)
    {
        LocalEventSyncToggleUi.Remove(__instance);
    }
}

[HarmonyPatch(typeof(NEventRoom), nameof(NEventRoom.OptionButtonClicked))]
internal static class NEventRoomOptionButtonPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NEventRoom __instance, EventOption option)
    {
        // 已在实机验证：该拦截用于保证涅奥/非共享事件必须双角色都完成后才可 Proceed。
        // 这里是开局主流程稳定点，后续若需调整请先做日志回归，避免回归到“仅一人可选”。
        if (!LocalSelfCoopContext.IsEnabled || !option.IsProceed || !RunManager.Instance.IsInProgress)
        {
            return true;
        }

        if (LocalSelfCoopContext.EventSyncAllEnabled)
        {
            return true;
        }

        if (LocalSelfCoopContext.UseSingleEventFlow)
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

internal static class LocalEventSyncToggleUi
{
    private const string ToggleButtonName = "LocalEventSyncAllToggleButton";

    internal static void Ensure(NEventRoom room)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        LocalSelfCoopContext.ResetEventSyncAllToggle("event-room-enter");

        LocalSimpleTextButton? button = room.GetNodeOrNull<LocalSimpleTextButton>(ToggleButtonName);
        if (button == null)
        {
            button = new LocalSimpleTextButton
            {
                Name = ToggleButtonName,
                CustomMinimumSize = new Vector2(220f, 34f),
                Size = new Vector2(220f, 34f),
                FontSize = 16,
                FocusMode = Control.FocusModeEnum.None,
                AnchorLeft = 1f,
                AnchorRight = 1f,
                AnchorTop = 0f,
                AnchorBottom = 0f,
                OffsetLeft = -240f,
                OffsetTop = 16f,
                OffsetRight = -20f,
                OffsetBottom = 50f,
                TopLevel = true,
                ZIndex = 90
            };
            LocalSimpleTextButton localButton = button;
            button.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NClickableControl>((_) =>
                {
                    bool changed = LocalSelfCoopContext.ToggleEventSyncAll("event-sync-all-button");
                    if (changed)
                    {
                        RefreshButtonText(localButton);
                    }
                }));
            room.AddChild(button);
        }

        RefreshButtonText(button);
    }

    internal static void Refresh(NEventRoom room)
    {
        LocalSimpleTextButton? button = room.GetNodeOrNull<LocalSimpleTextButton>(ToggleButtonName);
        if (button == null)
        {
            return;
        }

        RefreshButtonText(button);
    }

    internal static void Remove(NEventRoom room)
    {
        LocalSimpleTextButton? button = room.GetNodeOrNull<LocalSimpleTextButton>(ToggleButtonName);
        button?.QueueFree();
    }

    private static void RefreshButtonText(LocalSimpleTextButton button)
    {
        button.ButtonText = LocalSelfCoopContext.EventSyncAllEnabled ? "同步到全部: 开" : "同步到全部: 关";
    }
}
