using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RunLobby), nameof(RunLobby.AbandonRun))]
internal static class RunLobbyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(RunLobby __instance)
    {
        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        LocalMultiControlLogger.Info("本地双人模式接管 RunLobby.AbandonRun，跳过 NetHostGameService 强转路径。");
        AccessTools.Method(typeof(RunManager), "AbandonAndCleanUp")?.Invoke(RunManager.Instance, new object[] { false });
        return false;
    }
}
