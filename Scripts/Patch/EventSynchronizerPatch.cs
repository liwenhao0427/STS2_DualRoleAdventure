using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption))]
internal static class EventSynchronizerPatch
{
    [HarmonyPostfix]
    private static void Postfix(EventSynchronizer __instance, int index)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        try
        {
            INetGameService? netService = AccessTools.Field(typeof(EventSynchronizer), "_netService")?.GetValue(__instance) as INetGameService;
            if (netService is not LocalLoopbackHostGameService)
            {
                return;
            }

            IPlayerCollection? playerCollection = AccessTools.Field(typeof(EventSynchronizer), "_playerCollection")?.GetValue(__instance) as IPlayerCollection;
            if (playerCollection == null || playerCollection.Players.Count != 2)
            {
                return;
            }

            ulong controlledPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId ?? 0;
            int localSlot = playerCollection.Players.ToList().FindIndex((player) => player.NetId == controlledPlayerId);
            if (localSlot < 0)
            {
                return;
            }

            int otherSlot = (localSlot + 1) % 2;
            Player otherPlayer = playerCollection.Players[otherSlot];
            if (__instance.IsShared)
            {
                List<uint?>? votes = AccessTools.Field(typeof(EventSynchronizer), "_playerVotes")?.GetValue(__instance) as List<uint?>;
                if (votes == null || votes.Count < 2 || votes[otherSlot].HasValue)
                {
                    return;
                }

                votes[otherSlot] = (uint)index;
                LocalMultiControlLogger.Info($"事件自动代投: slot{otherSlot} -> option={index}");
                if (votes.All((vote) => vote.HasValue) && netService.Type != NetGameType.Client)
                {
                    AccessTools.Method(typeof(EventSynchronizer), "ChooseSharedEventOption")?.Invoke(__instance, Array.Empty<object>());
                }
            }
            else
            {
                EventModel otherEvent = __instance.GetEventForPlayer(otherPlayer);
                if (otherEvent.IsFinished || otherEvent.CurrentOptions.Count <= 0)
                {
                    return;
                }

                int mirroredIndex = Math.Clamp(index, 0, otherEvent.CurrentOptions.Count - 1);
                AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForEvent")?.Invoke(__instance, new object[] { otherPlayer, mirroredIndex });
                LocalMultiControlLogger.Info($"事件自动代选: player={otherPlayer.NetId}, option={mirroredIndex}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"事件自动代投失败: {exception.Message}");
        }
    }
}
