using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption))]
internal static class EventSynchronizerPatch
{
    private static bool _isChoosingSharedEventOption;

    private struct SenderState
    {
        internal bool IsPatched;
        internal ulong? PreviousContextNetId;
        internal ulong PreviousSenderId;
    }

    [HarmonyPrefix]
    private static void Prefix(EventSynchronizer __instance, int index, ref SenderState __state)
    {
        __state = default;
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleEventFlow)
        {
            return;
        }

        if (!__instance.IsShared)
        {
            return;
        }

        INetGameService? netService = AccessTools.Field(typeof(EventSynchronizer), "_netService")?.GetValue(__instance) as INetGameService;
        if (netService is not LocalLoopbackHostGameService loopbackService)
        {
            return;
        }

        __state.IsPatched = true;
        __state.PreviousContextNetId = LocalContext.NetId;
        __state.PreviousSenderId = loopbackService.NetId;

        LocalContext.NetId = LocalSelfCoopContext.PrimaryPlayerId;
        loopbackService.SetCurrentSenderId(LocalSelfCoopContext.PrimaryPlayerId);
    }

    [HarmonyPostfix]
    private static void Postfix(EventSynchronizer __instance, int index, SenderState __state)
    {
        try
        {
            TryAutoProxyEventChoice(__instance, index);
        }
        finally
        {
            if (__state.IsPatched)
            {
                INetGameService? netService = AccessTools.Field(typeof(EventSynchronizer), "_netService")?.GetValue(__instance) as INetGameService;
                if (netService is LocalLoopbackHostGameService loopbackService && loopbackService.NetId != __state.PreviousSenderId)
                {
                    loopbackService.SetCurrentSenderId(__state.PreviousSenderId);
                }

                LocalContext.NetId = __state.PreviousContextNetId;
            }
        }
    }

    private static void TryAutoProxyEventChoice(EventSynchronizer synchronizer, int index)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        if (!synchronizer.IsShared)
        {
            ulong currentPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId ?? 0;
            if (currentPlayerId != 0)
            {
                LocalSelfCoopContext.RequestEventAutoSwitchAfterChoice(currentPlayerId);
            }

            return;
        }

        if (!LocalSelfCoopContext.EventSyncAllEnabled)
        {
            TrySwitchToNextPendingSharedVotePlayer(synchronizer);
            return;
        }

        try
        {
            INetGameService? netService = AccessTools.Field(typeof(EventSynchronizer), "_netService")?.GetValue(synchronizer) as INetGameService;
            if (netService is not LocalLoopbackHostGameService)
            {
                return;
            }

            IPlayerCollection? playerCollection = AccessTools.Field(typeof(EventSynchronizer), "_playerCollection")?.GetValue(synchronizer) as IPlayerCollection;
            List<uint?>? votes = AccessTools.Field(typeof(EventSynchronizer), "_playerVotes")?.GetValue(synchronizer) as List<uint?>;
            if (playerCollection == null || votes == null)
            {
                return;
            }

            int sharedCount = Math.Min(playerCollection.Players.Count, votes.Count);
            if (sharedCount < 2)
            {
                return;
            }

            List<Player> players = playerCollection.Players.Take(sharedCount).ToList();
            ulong localPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId
                ?? LocalContext.NetId
                ?? LocalSelfCoopContext.PrimaryPlayerId;

            int localSlot = players.FindIndex((player) => player.NetId == localPlayerId);
            if (localSlot < 0)
            {
                localSlot = players.FindIndex((player) => player.NetId == LocalSelfCoopContext.PrimaryPlayerId);
            }

            if (localSlot < 0)
            {
                return;
            }

            uint selectedOption = (uint)index;
            votes[localSlot] = selectedOption;
            int filledCount = 1;
            for (int i = 0; i < sharedCount; i++)
            {
                if (i == localSlot)
                {
                    continue;
                }

                if (!votes[i].HasValue || votes[i]!.Value != selectedOption)
                {
                    votes[i] = selectedOption;
                }

                filledCount++;
            }

            LocalMultiControlLogger.Info($"共享事件自动补齐投票: option={index}, filled={filledCount}/{sharedCount}");
            if (votes.Take(sharedCount).All((vote) => vote.HasValue) && netService.Type != NetGameType.Client)
            {
                TryChooseSharedEventOptionDeferred(synchronizer);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"共享事件自动补票失败: {exception.Message}");
        }
    }

    private static void TrySwitchToNextPendingSharedVotePlayer(EventSynchronizer synchronizer)
    {
        try
        {
            IPlayerCollection? playerCollection = AccessTools.Field(typeof(EventSynchronizer), "_playerCollection")?.GetValue(synchronizer) as IPlayerCollection;
            List<uint?>? votes = AccessTools.Field(typeof(EventSynchronizer), "_playerVotes")?.GetValue(synchronizer) as List<uint?>;
            if (playerCollection == null || votes == null)
            {
                return;
            }

            int sharedCount = Math.Min(playerCollection.Players.Count, votes.Count);
            if (sharedCount < 2)
            {
                return;
            }

            List<Player> players = playerCollection.Players.Take(sharedCount).ToList();
            ulong currentPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId
                ?? LocalContext.NetId
                ?? LocalSelfCoopContext.PrimaryPlayerId;
            int currentSlot = players.FindIndex((player) => player.NetId == currentPlayerId);
            if (currentSlot < 0)
            {
                currentSlot = 0;
            }

            for (int step = 1; step < sharedCount; step++)
            {
                int slot = (currentSlot + step) % sharedCount;
                if (votes[slot].HasValue)
                {
                    continue;
                }

                ulong targetPlayerId = players[slot].NetId;
                Callable.From(delegate
                {
                    LocalMultiControlRuntime.SwitchControlledPlayerTo(targetPlayerId, "event-shared-next-player");
                }).CallDeferred();
                LocalMultiControlLogger.Info($"共享事件投票已切换到下一位待选角色: {currentPlayerId} -> {targetPlayerId}");
                return;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"共享事件待选角色切换失败: {exception.Message}");
        }
    }

    private static void TryChooseSharedEventOptionDeferred(EventSynchronizer synchronizer)
    {
        if (_isChoosingSharedEventOption)
        {
            LocalMultiControlLogger.Warn("共享事件结算已在进行中，跳过重复触发。");
            return;
        }

        _isChoosingSharedEventOption = true;
        Callable.From(delegate
        {
            try
            {
                AccessTools.Method(typeof(EventSynchronizer), "ChooseSharedEventOption")?.Invoke(synchronizer, Array.Empty<object>());
                LocalMultiControlLogger.Info("共享事件自动补票完成，已触发结算。");
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"共享事件结算触发失败: {exception.Message}");
            }
            finally
            {
                _isChoosingSharedEventOption = false;
            }
        }).CallDeferred();
    }
}
