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

    private static readonly List<ulong> _localPlayerIds = new() { 1, 2, 3, 4 };

    private static int _desiredLocalPlayerCount = 4;

    private static bool _isSyncingCharacterHighlight;

    private static ulong? _pendingEventAutoSwitchPlayerId;

    private static bool _eventAutoSwitchPending;

    public static bool UseSingleAdventureMode => true;

    public static bool UseSingleEventFlow => true;

    public static int DesiredLocalPlayerCount => _desiredLocalPlayerCount;

    public static IReadOnlyList<ulong> LocalPlayerIds => _localPlayerIds;

    public static ulong PrimaryPlayerId { get; private set; } = 1;
    // 保留兼容字段，旧代码仍可读取第二槽位。
    public static ulong SecondaryPlayerId { get; private set; } = 2;

    public static bool IsEnabled { get; private set; }

    public static LocalLoopbackHostGameService? NetService { get; private set; }

    public static ulong CurrentLobbyEditingPlayerId { get; private set; } = 1;

    public static NCharacterSelectScreen? ActiveCharacterSelectScreen { get; set; }

    public static ulong ResolvePrimaryPlayerId()
    {
        ulong localPlatformPlayerId = PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform);
        if (localPlatformPlayerId == 0)
        {
            localPlatformPlayerId = 1;
        }

        List<ulong> ids = BuildSequentialPlayerIds(localPlatformPlayerId, _desiredLocalPlayerCount);
        ApplyLocalPlayerIds(ids);
        LocalMultiControlLogger.Info($"鏈湴澶氭帶鐜╁ID宸茶В鏋? {string.Join(",", _localPlayerIds)}");
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
            LocalMultiControlLogger.Warn($"蹇界暐鏃犳晥瀛樻。鐜╁ID鍒楄〃: {string.Join(",", playerIds)}");
            return;
        }

        _desiredLocalPlayerCount = Math.Clamp(normalized.Count, MinLocalPlayerCount, MaxLocalPlayerCount);
        ApplyLocalPlayerIds(normalized);
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
        LocalMultiControlLogger.Info($"宸蹭粠瀛樻。鎭㈠鏈湴澶氭帶鐜╁ID: {string.Join(",", _localPlayerIds)}");
    }

    public static bool IsSaveOwnedByLocalSelfCoop(SerializableRun run)
    {
        if (run.Players.Count < MinLocalPlayerCount)
        {
            return false;
        }

        return _localPlayerIds
            .Take(_desiredLocalPlayerCount)
            .All((playerId) => run.Players.Any((player) => player.NetId == playerId));
    }

    public static void Enable(LocalLoopbackHostGameService netService)
    {
        IsEnabled = true;
        NetService = netService;
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
        EnsureLobbySenderContext("enable");
        LocalMultiControlLogger.Info($"鏈湴澶氭帶妯″紡宸插惎鐢紝鐩爣鐜╁鏁?{_desiredLocalPlayerCount}");
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
        LocalMultiControlLogger.Info($"鏈湴澶氭帶妯″紡宸插叧闂紝鍘熷洜: {reason}");
    }

    public static bool SwitchLobbyEditingPlayer(bool next)
    {
        if (!IsEnabled || NetService == null)
        {
            return false;
        }

        List<ulong> activePlayerIds = GetActiveLobbyLocalPlayerIds();
        if (activePlayerIds.Count < MinLocalPlayerCount)
        {
            return false;
        }

        int currentIndex = activePlayerIds.IndexOf(CurrentLobbyEditingPlayerId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        int delta = next ? 1 : -1;
        int targetIndex = (currentIndex + delta + activePlayerIds.Count) % activePlayerIds.Count;
        ulong previousPlayerId = CurrentLobbyEditingPlayerId;
        CurrentLobbyEditingPlayerId = activePlayerIds[targetIndex];

        EnsureLobbySenderContext("switch-lobby-editing-player");
        SyncCharacterSelectHighlight();

        string slotLabel = GetSlotLabel(CurrentLobbyEditingPlayerId);
        LocalMultiControlLogger.Info($"澶у巺缂栬緫瑙掕壊鍒囨崲: {previousPlayerId} -> {CurrentLobbyEditingPlayerId} (妲戒綅{slotLabel})");
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create($"澶у巺缂栬緫瑙掕壊: 妲戒綅{slotLabel}"));
        return true;
    }

    public static bool AdjustDesiredLocalPlayerCount(int delta, string source)
    {
        int oldCount = _desiredLocalPlayerCount;
        int targetCount = Math.Clamp(oldCount + delta, MinLocalPlayerCount, MaxLocalPlayerCount);
        if (targetCount == oldCount)
        {
            return false;
        }

        _desiredLocalPlayerCount = targetCount;
        EnsureLocalPlayerIdCapacity(targetCount);

        bool reconciled = ReconcileLobbyPlayerCount(source);
        if (!reconciled)
        {
            LocalMultiControlLogger.Info($"宸叉洿鏂扮洰鏍囨湰鍦扮帺瀹舵暟: {oldCount} -> {targetCount}");
        }

        List<ulong> saveIds = _localPlayerIds.Take(_desiredLocalPlayerCount).ToList();
        LocalSelfCoopSaveTag.MarkCurrentProfile(saveIds);
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

    public static bool EnsureLobbySenderContext(string source)
    {
        if (!IsEnabled || NetService == null)
        {
            return false;
        }

        EnsureLobbyEditingPlayerIsValid();
        NetService.SetCurrentSenderId(CurrentLobbyEditingPlayerId);
        LocalContext.NetId = CurrentLobbyEditingPlayerId;
        LocalMultiControlLogger.Info($"澶у巺鎺у埗涓婁笅鏂囧悓姝? player={CurrentLobbyEditingPlayerId}, source={source}");
        return true;
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
        LocalMultiControlLogger.Info($"璁板綍浜嬩欢鑷姩鍒囨崲璇锋眰: player={playerId}");
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
        ActiveCharacterSelectScreen = characterSelectScreen;
        return ReconcileLobbyPlayerCount("bootstrap-local-players");
    }

    private static bool ReconcileLobbyPlayerCount(string source)
    {
        if (!IsEnabled || NetService == null || ActiveCharacterSelectScreen == null)
        {
            return false;
        }

        NCharacterSelectScreen screen = ActiveCharacterSelectScreen;
        int targetCount = Math.Clamp(_desiredLocalPlayerCount, MinLocalPlayerCount, MaxLocalPlayerCount);
        EnsureLocalPlayerIdCapacity(targetCount);

        UnlockState unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
        SerializableUnlockState serializableUnlockState = unlockState.ToSerializable();
        int maxAscension = SaveManager.Instance.Progress.MaxMultiplayerAscension;

        List<ulong> targetPlayerIds = _localPlayerIds.Take(targetCount).ToList();

        foreach (ulong playerId in targetPlayerIds)
        {
            bool exists = screen.Lobby.Players.Any((player) => player.id == playerId);
            if (exists)
            {
                continue;
            }

            NetService.SetCurrentSenderId(playerId);
            _ = screen.Lobby.AddLocalHostPlayerInternal(serializableUnlockState, maxAscension);
        }

        List<ulong> removablePlayerIds = _localPlayerIds
            .Skip(targetCount)
            .Where((playerId) => playerId != PrimaryPlayerId)
            .ToList();
        foreach (ulong removableId in removablePlayerIds)
        {
            int playerIndex = screen.Lobby.Players.FindIndex((player) => player.id == removableId);
            if (playerIndex < 0)
            {
                continue;
            }

            LobbyPlayer removedPlayer = screen.Lobby.Players[playerIndex];
            screen.Lobby.Players.RemoveAt(playerIndex);
            screen.Lobby.InputSynchronizer.OnPlayerDisconnected(removedPlayer.id);
            screen.RemotePlayerDisconnected(removedPlayer);
        }

        foreach (ulong playerId in targetPlayerIds)
        {
            if (playerId == PrimaryPlayerId)
            {
                continue;
            }

            int playerIndex = screen.Lobby.Players.FindIndex((player) => player.id == playerId);
            if (playerIndex < 0)
            {
                continue;
            }

            LobbyPlayer lobbyPlayer = screen.Lobby.Players[playerIndex];
            if (lobbyPlayer.isReady)
            {
                continue;
            }

            lobbyPlayer.isReady = true;
            screen.Lobby.Players[playerIndex] = lobbyPlayer;
            screen.PlayerChanged(lobbyPlayer);
        }

        EnsureLobbyEditingPlayerIsValid();
        EnsureLobbySenderContext(source);
        SyncCharacterSelectHighlight();

        LocalMultiControlLogger.Info(
            $"澶у巺鏈湴鐜╁鏁板凡鍚屾: target={targetCount}, actual={GetActiveLobbyLocalPlayerIds().Count}, source={source}");
        return true;
    }

    private static void EnsureLobbyEditingPlayerIsValid()
    {
        List<ulong> activePlayerIds = GetActiveLobbyLocalPlayerIds();
        if (activePlayerIds.Count == 0)
        {
            activePlayerIds = _localPlayerIds.Take(_desiredLocalPlayerCount).ToList();
        }

        if (activePlayerIds.Count == 0)
        {
            CurrentLobbyEditingPlayerId = PrimaryPlayerId;
            return;
        }

        if (!activePlayerIds.Contains(CurrentLobbyEditingPlayerId))
        {
            CurrentLobbyEditingPlayerId = activePlayerIds[0];
        }
    }

    private static List<ulong> GetActiveLobbyLocalPlayerIds()
    {
        if (ActiveCharacterSelectScreen == null)
        {
            return _localPlayerIds.Take(_desiredLocalPlayerCount).ToList();
        }

        return ActiveCharacterSelectScreen.Lobby.Players
            .Where((player) => _localPlayerIds.Contains(player.id))
            .Select((player) => player.id)
            .Distinct()
            .OrderBy((playerId) => _localPlayerIds.IndexOf(playerId))
            .ToList();
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

            EnsureLobbyEditingPlayerIsValid();
            int localPlayerIndex = ActiveCharacterSelectScreen.Lobby.Players
                .FindIndex((player) => player.id == CurrentLobbyEditingPlayerId);
            if (localPlayerIndex < 0)
            {
                return;
            }

            LobbyPlayer localPlayer = ActiveCharacterSelectScreen.Lobby.Players[localPlayerIndex];
            Control? charButtonContainer = AccessTools.Field(typeof(NCharacterSelectScreen), "_charButtonContainer")
                ?.GetValue(ActiveCharacterSelectScreen) as Control;
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
            LocalMultiControlLogger.Warn($"鍚屾瑙掕壊閫夋嫨楂樹寒澶辫触: {exception.Message}");
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

    private static void EnsureLocalPlayerIdCapacity(int targetCount)
    {
        int clampedTargetCount = Math.Clamp(targetCount, MinLocalPlayerCount, MaxLocalPlayerCount);
        if (_localPlayerIds.Count >= clampedTargetCount)
        {
            return;
        }

        List<ulong> expanded = BuildSequentialPlayerIds(PrimaryPlayerId, clampedTargetCount);
        ApplyLocalPlayerIds(expanded);
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
        List<ulong> ids = new(targetCount) { primaryPlayerId == 0 ? 1UL : primaryPlayerId };
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

