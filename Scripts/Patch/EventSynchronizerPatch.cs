using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
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
        internal bool IsManualSelectionOption;
    }

    [HarmonyPrefix]
    private static void Prefix(EventSynchronizer __instance, int index, ref SenderState __state)
    {
        __state = default;
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleEventFlow)
        {
            return;
        }

        __state.IsManualSelectionOption = IsManualSelectionOption(__instance, index);

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
            TryAutoProxyEventChoice(__instance, index, __state);
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

    private static void TryAutoProxyEventChoice(EventSynchronizer synchronizer, int index, SenderState state)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        if (!synchronizer.IsShared)
        {
            if (LocalSelfCoopContext.UseSingleEventFlow)
            {
                if (ShouldSkipBroadcastForNeow(synchronizer))
                {
                    LocalMultiControlLogger.Info("检测到涅奥事件，跳过普通事件广播，保持各角色独立开局选项。");
                    return;
                }

                if (state.IsManualSelectionOption)
                {
                    LocalMultiControlLogger.Info("检测到需要手动选牌/选项的事件分支，跳过自动广播，改为切人后逐角色手动完成。");
                    return;
                }

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

    private static bool IsManualSelectionOption(EventSynchronizer synchronizer, int index)
    {
        try
        {
            EventModel localEvent = synchronizer.GetLocalEvent();
            if (localEvent.CurrentOptions.Count == 0 || index < 0 || index >= localEvent.CurrentOptions.Count)
            {
                return false;
            }

            EventOption option = localEvent.CurrentOptions[index];
            string textKey = option.TextKey ?? string.Empty;
            string description = option.Description?.GetRawText() ?? string.Empty;

            if (textKey.Contains("SCROLL_BOXES", StringComparison.OrdinalIgnoreCase) ||
                textKey.Contains("DARK", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (description.Contains("选择", StringComparison.Ordinal) ||
                description.Contains("移除", StringComparison.Ordinal) ||
                description.Contains("变形", StringComparison.Ordinal) ||
                description.Contains("升级一张", StringComparison.Ordinal) ||
                description.Contains("select", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("remove", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldSkipBroadcastForNeow(EventSynchronizer synchronizer)
    {
        try
        {
            EventModel localEvent = synchronizer.GetLocalEvent();
            string eventId = localEvent.Id.Entry;
            return string.Equals(eventId, "NEOW", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
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

        System.Reflection.FieldInfo? localPlayerIdField = AccessTools.Field(typeof(EventSynchronizer), "_localPlayerId");
        object? previousLocalPlayerIdValue = localPlayerIdField?.GetValue(synchronizer);
        ulong? previousContextNetId = LocalContext.NetId;
        ulong previousSenderId = loopbackService.NetId;
        try
        {
            foreach (Player targetPlayer in targetPlayers)
            {
                try
                {
                    localPlayerIdField?.SetValue(synchronizer, targetPlayer.NetId);
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
            if (localPlayerIdField != null)
            {
                localPlayerIdField.SetValue(synchronizer, previousLocalPlayerIdValue);
            }

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
