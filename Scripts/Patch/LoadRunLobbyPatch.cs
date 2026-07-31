using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
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

        List<ulong> localPlayerIdsInRun = __instance.Run.Players
            .Select((player) => player.NetId)
            .Where((id) => LocalSelfCoopContext.LocalPlayerIds.Contains(id))
            .Distinct()
            .ToList();
        if (localPlayerIdsInRun.Count <= 1)
        {
            return;
        }

        ulong localHostId = __instance.NetService.NetId;

        if (ready)
        {
            foreach (ulong playerId in localPlayerIdsInRun)
            {
                if (playerId == localHostId)
                {
                    continue;
                }

                int playerIndex = __instance.Players.FindIndex((player) => player.id == playerId);
                if (playerIndex < 0)
                {
                    LoadRunLobbyPlayer newPlayer = new()
                    {
                        id = playerId,
                        versionInfo = PeerVersionInfo.LocalDefault(),
                        isReady = true
                    };
                    __instance.Players.Add(newPlayer);
                    __instance.LobbyListener.PlayerConnected(newPlayer);
                    __instance.LobbyListener.PlayerReadyChanged(playerId);
                    continue;
                }

                LoadRunLobbyPlayer lobbyPlayer = __instance.Players[playerIndex];
                if (lobbyPlayer.isReady)
                {
                    continue;
                }

                lobbyPlayer.isReady = true;
                __instance.Players[playerIndex] = lobbyPlayer;
                __instance.LobbyListener.PlayerReadyChanged(playerId);
            }

            InvokeBeginRunIfAllPlayersReady(__instance);
            LocalMultiControlLogger.Info($"本地多控读档自动就绪: players={string.Join(",", localPlayerIdsInRun)}");
            return;
        }

        foreach (ulong playerId in localPlayerIdsInRun)
        {
            if (playerId == localHostId)
            {
                continue;
            }

            int playerIndex = __instance.Players.FindIndex((player) => player.id == playerId);
            if (playerIndex < 0)
            {
                continue;
            }

            LoadRunLobbyPlayer lobbyPlayer = __instance.Players[playerIndex];
            if (!lobbyPlayer.isReady)
            {
                continue;
            }

            lobbyPlayer.isReady = false;
            __instance.Players[playerIndex] = lobbyPlayer;
            __instance.LobbyListener.PlayerReadyChanged(playerId);
        }
    }

    private static void InvokeBeginRunIfAllPlayersReady(LoadRunLobby lobby)
    {
        if (AccessTools.Method(typeof(LoadRunLobby), "BeginRunForAllPlayersIfAllReady") is { } beginRunNew)
        {
            beginRunNew.Invoke(lobby, new object[] { });
            return;
        }

        if (AccessTools.Method(typeof(LoadRunLobby), "BeginRunIfAllPlayersReady") is { } beginRunLegacy)
        {
            beginRunLegacy.Invoke(lobby, new object[] { });
            return;
        }

        LocalMultiControlLogger.Warn("读档自动开局失败：未找到 BeginRunIfAllPlayersReady/BeginRunForAllPlayersIfAllReady。");
    }
}
