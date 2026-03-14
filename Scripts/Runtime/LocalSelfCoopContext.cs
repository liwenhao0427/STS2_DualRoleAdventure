using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalSelfCoopContext
{
    private const int MinLocalPlayerCount = 2;
    private const int MaxLocalPlayerCount = 4;

    private static readonly List<ulong> _localPlayerIds = new() { 1, 2 };

    public static bool UseSingleAdventureMode => true;

    public static bool UseSingleEventFlow => true;

    public static int DesiredLocalPlayerCount => MaxLocalPlayerCount;

    public static IReadOnlyList<ulong> LocalPlayerIds => _localPlayerIds;

    public static ulong PrimaryPlayerId { get; private set; } = 1;

    public static ulong SecondaryPlayerId { get; private set; } = 2;

    public static bool IsEnabled { get; private set; }

    public static LocalLoopbackHostGameService? NetService { get; private set; }

    public static ulong CurrentLobbyEditingPlayerId { get; private set; } = 1;

    public static NCharacterSelectScreen? ActiveCharacterSelectScreen { get; set; }

    private static bool _isSyncingCharacterHighlight;

    private static ulong? _pendingEventAutoSwitchPlayerId;

    private static bool _eventAutoSwitchPending;

    public static ulong ResolvePrimaryPlayerId()
    {
        ulong localPlatformPlayerId = PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform);
        if (localPlatformPlayerId == 0)
        {
            localPlatformPlayerId = 1;
        }

        List<ulong> ids = BuildSequentialPlayerIds(localPlatformPlayerId, DesiredLocalPlayerCount);
        ApplyLocalPlayerIds(ids);
        LocalMultiControlLogger.Info($"本地多控玩家ID已解析: {string.Join(",", _localPlayerIds)}");
        return PrimaryPlayerId;
    }

    public static void UseSavedPlayerIds(ulong primaryPlayerId, ulong secondaryPlayerId)
    {
        UseSavedPlayerIds(new List<ulong> { primaryPlayerId, secondaryPlayerId });
    }

    public static void UseSavedPlayerIds(IReadOnlyList<ulong> playerIds)
    {
        List<ulong> normalized = NormalizePlayerIds(playerIds, fallbackPrimaryId: PrimaryPlayerId);
        if (normalized.Count < MinLocalPlayerCount)
        {
            LocalMultiControlLogger.Warn($"忽略无效存档玩家ID列表: {string.Join(",", playerIds)}");
            return;
        }

        ApplyLocalPlayerIds(normalized);
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
        LocalMultiControlLogger.Info($"已从存档恢复本地多控玩家ID: {string.Join(",", _localPlayerIds)}");
    }

    public static bool IsSaveOwnedByLocalSelfCoop(SerializableRun run)
    {
        if (run.Players.Count < MinLocalPlayerCount)
        {
            return false;
        }

        return _localPlayerIds.All((playerId) => run.Players.Any((player) => player.NetId == playerId));
    }

    public static void Enable(LocalLoopbackHostGameService netService)
    {
        IsEnabled = true;
        NetService = netService;
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
        LocalContext.NetId = PrimaryPlayerId;
        LocalMultiControlLogger.Info($"本地多控模式已启用，玩家数={_localPlayerIds.Count}");
    }

    public static void Disable(string reason)
    {
        if (!IsEnabled && NetService == null)
        {
            return;
        }

        IsEnabled = false;
        NetService = null;
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
        ActiveCharacterSelectScreen = null;
        _pendingEventAutoSwitchPlayerId = null;
        _eventAutoSwitchPending = false;
        LocalMultiControlLogger.Info($"本地多控模式已关闭，原因: {reason}");
    }

    public static bool SwitchLobbyEditingPlayer(bool next)
    {
        if (!IsEnabled || NetService == null || _localPlayerIds.Count < MinLocalPlayerCount)
        {
            return false;
        }

        int currentIndex = _localPlayerIds.IndexOf(CurrentLobbyEditingPlayerId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        int delta = next ? 1 : -1;
        int targetIndex = (currentIndex + delta + _localPlayerIds.Count) % _localPlayerIds.Count;
        ulong previousPlayerId = CurrentLobbyEditingPlayerId;
        CurrentLobbyEditingPlayerId = _localPlayerIds[targetIndex];

        NetService.SetCurrentSenderId(CurrentLobbyEditingPlayerId);
        LocalContext.NetId = CurrentLobbyEditingPlayerId;
        SyncCharacterSelectHighlight();

        string slotLabel = GetSlotLabel(CurrentLobbyEditingPlayerId);
        LocalMultiControlLogger.Info($"大厅编辑角色切换: {previousPlayerId} -> {CurrentLobbyEditingPlayerId} (槽位{slotLabel})");
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create($"大厅编辑角色: 槽位{slotLabel}"));
        return true;
    }

    public static bool TryGetSlotIndex(ulong playerId, out int slotIndex)
    {
        slotIndex = _localPlayerIds.IndexOf(playerId);
        return slotIndex >= 0;
    }

    public static string GetSlotLabel(ulong playerId)
    {
        return TryGetSlotIndex(playerId, out int slotIndex)
            ? (slotIndex + 1).ToString()
            : "?";
    }

    public static void NotifyCharacterSelectPlayerChanged(ulong playerId)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (playerId == CurrentLobbyEditingPlayerId)
        {
            SyncCharacterSelectHighlight();
        }
    }

    public static void RequestEventAutoSwitchAfterChoice(ulong playerId)
    {
        if (!IsEnabled)
        {
            return;
        }

        _pendingEventAutoSwitchPlayerId = playerId;
        LocalMultiControlLogger.Info($"记录事件自动切换请求: player={playerId}");
    }

    public static bool ShouldQueueEventAutoSwitchAfterEventState(EventModel eventModel)
    {
        if (!IsEnabled || !_pendingEventAutoSwitchPlayerId.HasValue || eventModel.Owner == null)
        {
            return false;
        }

        if (!eventModel.IsFinished || eventModel.Owner.NetId != _pendingEventAutoSwitchPlayerId.Value)
        {
            return false;
        }

        _pendingEventAutoSwitchPlayerId = null;
        _eventAutoSwitchPending = true;
        return true;
    }

    public static bool TryConsumePendingEventAutoSwitch()
    {
        if (!_eventAutoSwitchPending)
        {
            return false;
        }

        _eventAutoSwitchPending = false;
        return true;
    }

    public static bool BootstrapSecondPlayer(NCharacterSelectScreen characterSelectScreen)
    {
        // 保留旧方法名，兼容已有调用。
        return BootstrapLocalPlayers(characterSelectScreen);
    }

    public static bool BootstrapLocalPlayers(NCharacterSelectScreen characterSelectScreen)
    {
        if (!IsEnabled || NetService == null)
        {
            LocalMultiControlLogger.Warn("尝试注入本地玩家失败：本地多控模式未启用。");
            return false;
        }

        int targetCount = Math.Clamp(DesiredLocalPlayerCount, MinLocalPlayerCount, MaxLocalPlayerCount);
        if (characterSelectScreen.Lobby.Players.Count >= targetCount)
        {
            LocalMultiControlLogger.Info($"角色选择大厅玩家数已满足目标: count={characterSelectScreen.Lobby.Players.Count}, target={targetCount}");
            return true;
        }

        UnlockState unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
        SerializableUnlockState serializableUnlockState = unlockState.ToSerializable();
        int maxAscension = SaveManager.Instance.Progress.MaxMultiplayerAscension;

        for (int i = 1; i < targetCount && i < _localPlayerIds.Count; i++)
        {
            ulong playerId = _localPlayerIds[i];
            bool alreadyExists = characterSelectScreen.Lobby.Players.Any((player) => player.id == playerId);
            if (alreadyExists)
            {
                continue;
            }

            NetService.SetCurrentSenderId(playerId);
            _ = characterSelectScreen.Lobby.AddLocalHostPlayerInternal(serializableUnlockState, maxAscension);
        }

        NetService.SetCurrentSenderId(PrimaryPlayerId);

        bool allReady = true;
        for (int i = 1; i < targetCount && i < _localPlayerIds.Count; i++)
        {
            ulong playerId = _localPlayerIds[i];
            int playerIndex = characterSelectScreen.Lobby.Players.FindIndex((player) => player.id == playerId);
            if (playerIndex < 0)
            {
                allReady = false;
                continue;
            }

            LobbyPlayer lobbyPlayer = characterSelectScreen.Lobby.Players[playerIndex];
            lobbyPlayer.isReady = true;
            characterSelectScreen.Lobby.Players[playerIndex] = lobbyPlayer;
            characterSelectScreen.PlayerChanged(lobbyPlayer);
        }

        LocalMultiControlLogger.Info(
            $"初始化本地队伍完成: target={targetCount}, actual={characterSelectScreen.Lobby.Players.Count}, players={string.Join(",", characterSelectScreen.Lobby.Players.Select((player) => player.id))}");
        return allReady;
    }

    private static void SyncCharacterSelectHighlight()
    {
        if (ActiveCharacterSelectScreen == null || _isSyncingCharacterHighlight)
        {
            return;
        }

        try
        {
            _isSyncingCharacterHighlight = true;
            LobbyPlayer localPlayer = ActiveCharacterSelectScreen.Lobby.LocalPlayer;
            Control? charButtonContainer = AccessTools.Field(typeof(NCharacterSelectScreen), "_charButtonContainer")?.GetValue(ActiveCharacterSelectScreen) as Control;
            if (charButtonContainer == null)
            {
                return;
            }

            List<NCharacterSelectButton> buttons = charButtonContainer.GetChildren().OfType<NCharacterSelectButton>().ToList();
            foreach (NCharacterSelectButton button in buttons)
            {
                foreach (LobbyPlayer player in ActiveCharacterSelectScreen.Lobby.Players)
                {
                    button.OnRemotePlayerDeselected(player.id);
                }
            }

            NCharacterSelectButton? selectedButton = null;
            foreach (NCharacterSelectButton button in buttons)
            {
                bool isSelected = button.Character == localPlayer.character;
                AccessTools.Field(typeof(NCharacterSelectButton), "_isSelected")?.SetValue(button, isSelected);
                if (isSelected)
                {
                    selectedButton = button;
                }
            }

            foreach (LobbyPlayer player in ActiveCharacterSelectScreen.Lobby.Players)
            {
                if (player.id == localPlayer.id)
                {
                    continue;
                }

                NCharacterSelectButton? targetButton = buttons.FirstOrDefault((button) => button.Character == player.character);
                targetButton?.OnRemotePlayerSelected(player.id);
            }

            foreach (NCharacterSelectButton button in buttons)
            {
                AccessTools.Method(typeof(NCharacterSelectButton), "RefreshState")?.Invoke(button, Array.Empty<object>());
            }

            AccessTools.Field(typeof(NCharacterSelectScreen), "_selectedButton")?.SetValue(ActiveCharacterSelectScreen, selectedButton);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"同步角色选择高亮失败: {exception.Message}");
        }
        finally
        {
            _isSyncingCharacterHighlight = false;
        }
    }

    private static void ApplyLocalPlayerIds(IReadOnlyList<ulong> playerIds)
    {
        _localPlayerIds.Clear();
        _localPlayerIds.AddRange(playerIds.Distinct().Take(MaxLocalPlayerCount));
        if (_localPlayerIds.Count < MinLocalPlayerCount)
        {
            _localPlayerIds.Clear();
            _localPlayerIds.Add(1);
            _localPlayerIds.Add(2);
        }

        PrimaryPlayerId = _localPlayerIds[0];
        SecondaryPlayerId = _localPlayerIds.Count > 1 ? _localPlayerIds[1] : _localPlayerIds[0];
    }

    private static List<ulong> NormalizePlayerIds(IReadOnlyList<ulong> playerIds, ulong fallbackPrimaryId)
    {
        List<ulong> normalized = playerIds
            .Where((id) => id != 0)
            .Distinct()
            .Take(MaxLocalPlayerCount)
            .ToList();

        if (normalized.Count >= MinLocalPlayerCount)
        {
            return normalized;
        }

        ulong primary = fallbackPrimaryId == 0 ? 1UL : fallbackPrimaryId;
        return BuildSequentialPlayerIds(primary, MinLocalPlayerCount);
    }

    private static List<ulong> BuildSequentialPlayerIds(ulong primaryPlayerId, int count)
    {
        int targetCount = Math.Clamp(count, MinLocalPlayerCount, MaxLocalPlayerCount);
        List<ulong> ids = new List<ulong>(targetCount) { primaryPlayerId == 0 ? 1UL : primaryPlayerId };
        while (ids.Count < targetCount)
        {
            ulong nextId = ids[^1] == ulong.MaxValue ? 1UL : ids[^1] + 1UL;
            while (nextId == 0 || ids.Contains(nextId))
            {
                nextId = nextId == ulong.MaxValue ? 1UL : nextId + 1UL;
            }

            ids.Add(nextId);
        }

        return ids;
    }
}
