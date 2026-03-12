using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalSelfCoopContext
{
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

        PrimaryPlayerId = localPlatformPlayerId;
        SecondaryPlayerId = localPlatformPlayerId == ulong.MaxValue ? localPlatformPlayerId - 1 : localPlatformPlayerId + 1;
        LocalMultiControlLogger.Info($"本地双人ID已解析: primary={PrimaryPlayerId}, secondary={SecondaryPlayerId}");
        return PrimaryPlayerId;
    }

    public static void Enable(LocalLoopbackHostGameService netService)
    {
        IsEnabled = true;
        NetService = netService;
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
        LocalContext.NetId = PrimaryPlayerId;
        LocalMultiControlLogger.Info("本地双人联机模式已启用。");
    }

    public static void Disable(string reason)
    {
        if (!IsEnabled && NetService == null)
        {
            return;
        }

        LocalMultiControlLogger.Info($"本地双人联机模式已关闭，原因: {reason}");
        IsEnabled = false;
        NetService = null;
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
        ActiveCharacterSelectScreen = null;
        _pendingEventAutoSwitchPlayerId = null;
        _eventAutoSwitchPending = false;
    }

    public static bool SwitchLobbyEditingPlayer(bool next)
    {
        if (!IsEnabled || NetService == null)
        {
            return false;
        }

        ulong previous = CurrentLobbyEditingPlayerId;
        CurrentLobbyEditingPlayerId = next
            ? (CurrentLobbyEditingPlayerId == PrimaryPlayerId ? SecondaryPlayerId : PrimaryPlayerId)
            : (CurrentLobbyEditingPlayerId == PrimaryPlayerId ? SecondaryPlayerId : PrimaryPlayerId);
        NetService.SetCurrentSenderId(CurrentLobbyEditingPlayerId);
        LocalContext.NetId = CurrentLobbyEditingPlayerId;
        SyncCharacterSelectHighlight();
        LocalMultiControlLogger.Info($"大厅编辑角色切换: {previous} -> {CurrentLobbyEditingPlayerId}");
        string slotLabel = CurrentLobbyEditingPlayerId == PrimaryPlayerId ? "1" : "2";
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create($"大厅编辑角色: 槽位{slotLabel}"));
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

    public static bool BootstrapSecondPlayer(NCharacterSelectScreen characterSelectScreen)
    {
        if (!IsEnabled || NetService == null)
        {
            LocalMultiControlLogger.Warn("尝试注入第二本地玩家失败：本地双人联机模式未启用。");
            return false;
        }

        if (characterSelectScreen.Lobby.Players.Count >= 2)
        {
            LocalMultiControlLogger.Info("角色选择大厅已存在2名及以上玩家，跳过注入第二本地玩家。");
            return true;
        }

        UnlockState unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
        int maxAscension = SaveManager.Instance.Progress.MaxMultiplayerAscension;

        NetService.SetCurrentSenderId(SecondaryPlayerId);
        SerializableUnlockState serializableUnlockState = unlockState.ToSerializable();
        _ = characterSelectScreen.Lobby.AddLocalHostPlayerInternal(serializableUnlockState, maxAscension);
        NetService.SetCurrentSenderId(PrimaryPlayerId);

        int secondPlayerIndex = characterSelectScreen.Lobby.Players.FindIndex((player) => player.id == SecondaryPlayerId);
        if (secondPlayerIndex < 0)
        {
            LocalMultiControlLogger.Error("注入第二本地玩家失败：未在 Lobby 中找到 playerId=2。");
            return false;
        }

        LobbyPlayer secondPlayer = characterSelectScreen.Lobby.Players[secondPlayerIndex];
        secondPlayer.isReady = true;
        characterSelectScreen.Lobby.Players[secondPlayerIndex] = secondPlayer;
        characterSelectScreen.PlayerChanged(secondPlayer);

        LocalMultiControlLogger.Info($"初始化2人本地队伍成功。玩家: {string.Join(",", characterSelectScreen.Lobby.Players.Select((player) => player.id))}");
        return true;
    }
}
