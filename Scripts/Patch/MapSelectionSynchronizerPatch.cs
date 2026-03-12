using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.PlayerVotedForMapCoord))]
internal static class MapSelectionSynchronizerPatch
{
    [HarmonyPostfix]
    private static void Postfix(MapSelectionSynchronizer __instance, MapVote? destination)
    {
        if (!LocalSelfCoopContext.IsEnabled || destination == null)
        {
            return;
        }

        try
        {
            INetGameService? netService = AccessTools.Field(typeof(MapSelectionSynchronizer), "_netService")?.GetValue(__instance) as INetGameService;
            if (netService is not LocalLoopbackHostGameService)
            {
                return;
            }

            RunState? runState = AccessTools.Field(typeof(MapSelectionSynchronizer), "_runState")?.GetValue(__instance) as RunState;
            if (runState == null || runState.Players.Count != 2)
            {
                return;
            }

            List<MapVote?>? votes = AccessTools.Field(typeof(MapSelectionSynchronizer), "_votes")?.GetValue(__instance) as List<MapVote?>;
            if (votes == null || votes.Count != 2)
            {
                return;
            }

            int localIndex = -1;
            ulong currentControlledPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId ?? 0;
            for (int i = 0; i < runState.Players.Count; i++)
            {
                if (runState.Players[i].NetId == currentControlledPlayerId)
                {
                    localIndex = i;
                    break;
                }
            }
            if (localIndex < 0)
            {
                return;
            }

            int otherIndex = (localIndex + 1) % 2;
            if (votes[otherIndex].HasValue)
            {
                return;
            }

            votes[otherIndex] = destination;
            LocalMultiControlLogger.Info($"地图自动跟投: slot{otherIndex} -> {destination}");

            if (votes.All((vote) => vote.HasValue && vote.Value.mapGenerationCount == __instance.MapGenerationCount) && netService.Type != NetGameType.Client)
            {
                AccessTools.Method(typeof(MapSelectionSynchronizer), "MoveToMapCoord")?.Invoke(__instance, Array.Empty<object>());
                LocalMultiControlLogger.Info("地图自动跟投完成，已触发路线推进。");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error($"地图自动跟投失败: {exception}");
        }
    }
}
