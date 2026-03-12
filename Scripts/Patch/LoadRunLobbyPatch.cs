using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(LoadRunLobby), nameof(LoadRunLobby.SetReady))]
internal static class LoadRunLobbyPatch
{
    [HarmonyPostfix]
    private static void Postfix(LoadRunLobby __instance, bool ready)
    {
        if (__instance.NetService is not LocalLoopbackHostGameService || !LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        ulong secondaryPlayerId = LocalSelfCoopContext.SecondaryPlayerId;
        if (__instance.Run.Players.All((player) => player.NetId != secondaryPlayerId))
        {
            LocalMultiControlLogger.Warn($"读档大厅未找到本地2号位玩家: {secondaryPlayerId}");
            return;
        }

        HashSet<ulong>? readyPlayers = AccessTools.Field(typeof(LoadRunLobby), "_readyPlayers")?.GetValue(__instance) as HashSet<ulong>;
        if (ready)
        {
            if (__instance.ConnectedPlayerIds.Add(secondaryPlayerId))
            {
                __instance.LobbyListener.PlayerConnected(secondaryPlayerId);
            }

            if (readyPlayers != null && readyPlayers.Add(secondaryPlayerId))
            {
                __instance.LobbyListener.PlayerReadyChanged(secondaryPlayerId);
            }

            AccessTools.Method(typeof(LoadRunLobby), "BeginRunIfAllPlayersReady")?.Invoke(__instance, new object[] { });
            LocalMultiControlLogger.Info($"本地双人读档自动就绪: 已补齐玩家 {secondaryPlayerId}");
            return;
        }

        if (readyPlayers != null && readyPlayers.Remove(secondaryPlayerId))
        {
            __instance.LobbyListener.PlayerReadyChanged(secondaryPlayerId);
        }
    }
}
