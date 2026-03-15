using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    private static int _isBroadcastingLocalEventOption;

    private struct SenderState
    {
        internal bool IsPatched;
        internal ulong? PreviousContextNetId;
        internal ulong PreviousSenderId;
    }

    [HarmonyPrefix]
    private static void Prefix(EventSynchronizer __instance, ref SenderState __state)
    {
        __state = default;
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleEventFlow)
        {
            return;
        }

        if (Volatile.Read(ref _isBroadcastingLocalEventOption) != 0)
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
            if (LocalSelfCoopContext.UseSingleEventFlow)
            {
                TryBroadcastNonSharedEventChoice(synchronizer, index);
                return;
            }

            if (!LocalSelfCoopContext.UseSingleEventFlow)
            {
                ulong currentPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId ?? 0;
                if (currentPlayerId != 0)
                {
                    LocalSelfCoopContext.RequestEventAutoSwitchAfterChoice(currentPlayerId);
                }
            }

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

    private static void TryBroadcastNonSharedEventChoice(EventSynchronizer synchronizer, int index)
    {
        if (Volatile.Read(ref _isBroadcastingLocalEventOption) != 0)
        {
            return;
        }

        INetGameService? netService = AccessTools.Field(typeof(EventSynchronizer), "_netService")?.GetValue(synchronizer) as INetGameService;
        if (netService is not LocalLoopbackHostGameService loopbackService)
        {
            return;
        }

        IPlayerCollection? playerCollection = AccessTools.Field(typeof(EventSynchronizer), "_playerCollection")?.GetValue(synchronizer) as IPlayerCollection;
        if (playerCollection == null || playerCollection.Players.Count <= 1)
        {
            return;
        }

        ulong sourcePlayerId = LocalContext.NetId
            ?? LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId
            ?? LocalSelfCoopContext.PrimaryPlayerId;
        List<Player> targetPlayers = playerCollection.Players
            .Where((player) => player.NetId != sourcePlayerId)
            .ToList();
        if (targetPlayers.Count == 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _isBroadcastingLocalEventOption, 1, 0) != 0)
        {
            return;
        }

        ulong? previousContextNetId = LocalContext.NetId;
        ulong previousSenderId = loopbackService.NetId;
        try
        {
            foreach (Player targetPlayer in targetPlayers)
            {
                try
                {
                    LocalContext.NetId = targetPlayer.NetId;
                    loopbackService.SetCurrentSenderId(targetPlayer.NetId);
                    synchronizer.ChooseLocalOption(index);
                    LocalMultiControlLogger.Info(
                        $"普通事件选项已广播到本地角色: source={sourcePlayerId}, target={targetPlayer.NetId}, option={index}");
                }
                catch (Exception exception)
                {
                    LocalMultiControlLogger.Warn(
                        $"普通事件选项广播失败: source={sourcePlayerId}, target={targetPlayer.NetId}, option={index}, error={exception.Message}");
                }
            }
        }
        finally
        {
            loopbackService.SetCurrentSenderId(previousSenderId);
            LocalContext.NetId = previousContextNetId;
            Volatile.Write(ref _isBroadcastingLocalEventOption, 0);
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
