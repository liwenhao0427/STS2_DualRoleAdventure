using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalMultiControlRuntime
{
    private static readonly LocalMultiSessionState Session = new LocalMultiSessionState();

    public static LocalMultiSessionState SessionState => Session;

    public static void OnRunLaunched(RunState runState)
    {
        LocalMultiControlLogger.Info("检测到 RunManager.Launch，开始初始化本地多控会话。");
        Session.InitializeFromRunState(runState);
        if (Session.CurrentControlledPlayerId.HasValue)
        {
            ApplyControlContext("run-launched");
        }
        else
        {
            LocalMultiControlLogger.Info("当前运行未启用本地多控会话。");
        }
    }

    public static void OnRunCleanup()
    {
        Session.Reset("RunManager.CleanUp");
        LocalSelfCoopContext.Disable("RunManager.CleanUp");
        LocalMultiControlLogger.Info("RunManager.CleanUp 后已完成本地多控会话清理。");
    }

    public static void SwitchNextControlledPlayer(string source)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        if (Session.SwitchNextPlayer())
        {
            ApplyControlContext(source);
        }
    }

    public static void SwitchPreviousControlledPlayer(string source)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        if (Session.SwitchPreviousPlayer())
        {
            ApplyControlContext(source);
        }
    }

    private static void ApplyControlContext(string source)
    {
        ulong? currentControlledPlayerId = Session.CurrentControlledPlayerId;
        if (!currentControlledPlayerId.HasValue)
        {
            return;
        }

        ulong? previousNetId = LocalContext.NetId;
        LocalContext.NetId = currentControlledPlayerId.Value;
        LocalSelfCoopContext.NetService?.SetCurrentSenderId(currentControlledPlayerId.Value);
        LocalMultiControlLogger.Info($"控制上下文已更新: {previousNetId?.ToString() ?? "null"} -> {currentControlledPlayerId.Value}, source={source}");
    }
}
