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

        List<ulong> localPlayerIdsInRun = __instance.Run.Players
            .Select((player) => player.NetId)
            .Where((id) => LocalSelfCoopContext.LocalPlayerIds.Contains(id))
            .Distinct()
            .ToList();
        if (localPlayerIdsInRun.Count <= 1)
        {
            return;
        }

        HashSet<ulong>? readyPlayers = AccessTools.Field(typeof(LoadRunLobby), "_readyPlayers")?.GetValue(__instance) as HashSet<ulong>;
        ulong localHostId = __instance.NetService.NetId;

        if (ready)
        {
            foreach (ulong playerId in localPlayerIdsInRun)
            {
                if (playerId == localHostId)
                {
                    continue;
                }

                if (__instance.ConnectedPlayerIds.Add(playerId))
                {
                    __instance.LobbyListener.PlayerConnected(playerId);
                }

                if (readyPlayers != null && readyPlayers.Add(playerId))
                {
                    __instance.LobbyListener.PlayerReadyChanged(playerId);
                }
            }

            AccessTools.Method(typeof(LoadRunLobby), "BeginRunIfAllPlayersReady")?.Invoke(__instance, new object[] { });
            LocalMultiControlLogger.Info($"本地多控读档自动就绪: players={string.Join(",", localPlayerIdsInRun)}");
            return;
        }

        if (readyPlayers == null)
        {
            return;
        }

        foreach (ulong playerId in localPlayerIdsInRun)
        {
            if (playerId == localHostId)
            {
                continue;
            }

            if (readyPlayers.Remove(playerId))
            {
                __instance.LobbyListener.PlayerReadyChanged(playerId);
            }
        }
    }
}
