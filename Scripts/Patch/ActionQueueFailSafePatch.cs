using System;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCardPlayQueue), "OnActionEnqueued")]
internal static class NCardPlayQueueOnActionEnqueuedFailSafePatch
{
    private static ulong _lastLogAtMs;

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, GameAction action)
    {
        if (!LocalSelfCoopContext.IsEnabled || __exception is not NullReferenceException)
        {
            return __exception;
        }

        ulong now = Time.GetTicksMsec();
        if (now - _lastLogAtMs >= 600)
        {
            _lastLogAtMs = now;
            LocalMultiControlLogger.Warn(
                $"动作队列UI入队触发空引用，已拦截避免阻塞: action={action?.ToString() ?? "null"}, context={LocalContext.NetId?.ToString() ?? "null"}");
        }

        TryRecoverActionOwnerContext();
        return null;
    }

    private static void TryRecoverActionOwnerContext()
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        ulong playerId = LocalContext.NetId ?? LocalSelfCoopContext.PrimaryPlayerId;
        if (playerId == 0)
        {
            return;
        }

        LocalMultiControlRuntime.AlignContextForActionOwner(playerId, "action-queue-ui-failsafe");
    }
}

[HarmonyPatch(typeof(ActionQueueSynchronizer), "RequestEnqueue", new[] { typeof(GameAction) })]
internal static class ActionQueueSynchronizerRequestEnqueueFailSafePatch
{
    private static ulong _lastLogAtMs;

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, GameAction action)
    {
        if (!LocalSelfCoopContext.IsEnabled || __exception is not NullReferenceException)
        {
            return __exception;
        }

        ulong now = Time.GetTicksMsec();
        if (now - _lastLogAtMs >= 600)
        {
            _lastLogAtMs = now;
            LocalMultiControlLogger.Warn(
                $"RequestEnqueue 空引用已拦截，避免阻塞: action={action?.ToString() ?? "null"}, context={LocalContext.NetId?.ToString() ?? "null"}");
        }

        ulong playerId = LocalContext.NetId ?? LocalSelfCoopContext.PrimaryPlayerId;
        if (playerId != 0)
        {
            LocalMultiControlRuntime.AlignContextForActionOwner(playerId, "request-enqueue-failsafe");
        }

        return null;
    }
}
