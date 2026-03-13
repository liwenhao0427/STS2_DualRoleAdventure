using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption))]
internal static class EventSynchronizerPatch
{
    [HarmonyPrefix]
    private static void Prefix(EventSynchronizer __instance, ref ulong __state)
    {
        __state = 0;
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleEventFlow)
        {
            return;
        }

        INetGameService? netService = AccessTools.Field(typeof(EventSynchronizer), "_netService")?.GetValue(__instance) as INetGameService;
        if (netService is not LocalLoopbackHostGameService loopbackService)
        {
            return;
        }

        __state = loopbackService.NetId;
        if (__state != LocalSelfCoopContext.PrimaryPlayerId)
        {
            loopbackService.SetCurrentSenderId(LocalSelfCoopContext.PrimaryPlayerId);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(EventSynchronizer __instance, int index)
    {
        if (LocalSelfCoopContext.IsEnabled && LocalSelfCoopContext.UseSingleEventFlow)
        {
            return;
        }

        PostfixLegacy(__instance, index);
    }

    [HarmonyPostfix]
    private static void PostfixRestoreSender(EventSynchronizer __instance, ulong __state)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleEventFlow || __state == 0)
        {
            return;
        }

        INetGameService? netService = AccessTools.Field(typeof(EventSynchronizer), "_netService")?.GetValue(__instance) as INetGameService;
        if (netService is LocalLoopbackHostGameService loopbackService && loopbackService.NetId != __state)
        {
            loopbackService.SetCurrentSenderId(__state);
        }
    }

    private static void PostfixLegacy(EventSynchronizer __instance, int index)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        if (!__instance.IsShared)
        {
            ulong currentPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId ?? 0;
            if (currentPlayerId != 0)
            {
                LocalSelfCoopContext.RequestEventAutoSwitchAfterChoice(currentPlayerId);
            }

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
            List<uint?>? votes = AccessTools.Field(typeof(EventSynchronizer), "_playerVotes")?.GetValue(__instance) as List<uint?>;
            if (playerCollection == null || votes == null || playerCollection.Players.Count != 2 || votes.Count < 2)
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
            if (votes[otherSlot].HasValue)
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
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"事件自动代投失败: {exception.Message}");
        }
    }
}
