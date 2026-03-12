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
            LocalMultiControlLogger.Info($"当前操控玩家已设定为: {Session.CurrentControlledPlayerId.Value}");
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
}
