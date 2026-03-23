using System;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

internal static class ActionQueueStallWatchdogRuntime
{
    private const ulong StallTimeoutMs = 5000UL;
    private const ulong RetryHandleIntervalMs = 2000UL;

    private static int _trackedActionIdentity;
    private static uint? _trackedActionId;
    private static ulong _trackedSinceMs;
    private static ulong _lastHandleMs;

    public static void Tick()
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            Reset();
            return;
        }

        GameAction? currentAction = RunManager.Instance.ActionExecutor.CurrentlyRunningAction;
        if (currentAction == null)
        {
            Reset();
            return;
        }

        int actionIdentity = RuntimeHelpers.GetHashCode(currentAction);
        uint? actionId = currentAction.Id;
        ulong nowMs = Time.GetTicksMsec();
        if (_trackedActionIdentity != actionIdentity || _trackedActionId != actionId)
        {
            _trackedActionIdentity = actionIdentity;
            _trackedActionId = actionId;
            _trackedSinceMs = nowMs;
            _lastHandleMs = 0UL;
            return;
        }

        ulong elapsedMs = nowMs - _trackedSinceMs;
        if (elapsedMs < StallTimeoutMs)
        {
            return;
        }

        if (_lastHandleMs > 0UL && nowMs - _lastHandleMs < RetryHandleIntervalMs)
        {
            return;
        }

        _lastHandleMs = nowMs;
        HandleStuckAction(currentAction, elapsedMs);
    }

    private static void HandleStuckAction(GameAction action, ulong elapsedMs)
    {
        try
        {
            if (action.State == GameActionState.GatheringPlayerChoice && action.Id.HasValue)
            {
                RunManager.Instance.ActionQueueSynchronizer.RequestResumeActionAfterPlayerChoice(action);
                LocalMultiControlLogger.Warn(
                    $"动作队列卡死兜底触发：强制恢复等待选择的动作: action={action}, id={action.Id.Value}, elapsedMs={elapsedMs}");
                return;
            }

            action.Cancel();
            LocalMultiControlLogger.Warn(
                $"动作队列卡死兜底触发：强制取消阻塞动作: action={action}, id={action.Id?.ToString() ?? "none"}, state={action.State}, elapsedMs={elapsedMs}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"动作队列卡死兜底执行失败: action={action}, id={action.Id?.ToString() ?? "none"}, error={exception.Message}");
        }
    }

    private static void Reset()
    {
        _trackedActionIdentity = 0;
        _trackedActionId = null;
        _trackedSinceMs = 0UL;
        _lastHandleMs = 0UL;
    }
}
