using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu.StartHost))]
internal static class NMultiplayerHostSubmenuCustomRunPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NMultiplayerHostSubmenu __instance, GameMode gameMode)
    {
        if (gameMode != GameMode.Custom)
        {
            return true;
        }

        NSubmenuStack? stack = AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(__instance) as NSubmenuStack;
        if (stack == null)
        {
            return true;
        }

        LocalMultiControlLogger.Info("联机自定义模式改走本地多角色回环开局。");
        LocalSelfCoopSaveTag.ClearCurrentProfile();
        SaveManager.Instance.DeleteCurrentMultiplayerRun();

        ulong primaryPlayerId = LocalSelfCoopContext.ResolvePrimaryPlayerId();
        LocalSelfCoopContext.UseSavedWakuuPlayerIds(Array.Empty<ulong>());
        LocalSelfCoopSaveTag.MarkCurrentProfile(
            LocalSelfCoopContext.LocalPlayerIds.Take(LocalSelfCoopContext.DesiredLocalPlayerCount).ToList());

        LocalLoopbackHostGameService netService = new(primaryPlayerId);
        LocalSelfCoopContext.Enable(netService);

        NCustomRunScreen customRunScreen = stack.GetSubmenuType<NCustomRunScreen>();
        customRunScreen.InitializeMultiplayerAsHost(netService, LocalSelfCoopContext.LocalPlayerIds.Count);
        stack.Push(customRunScreen);
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(LocalModText.EnteredLocalSelfCoopHint));
        return false;
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.OnSubmenuOpened))]
internal static class NCustomRunScreenLocalPlayersPatch
{
    private const int MaxLocalAscensionLevel = 10;

    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance)
    {
        TryReconcileLocalPlayers(__instance);
    }

    private static void TryReconcileLocalPlayers(NCustomRunScreen screen)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        StartRunLobby lobby = screen.Lobby;
        if (lobby.NetService is not LocalLoopbackHostGameService loopbackService)
        {
            return;
        }

        EnsureLobbyMaxCapacity(lobby);
        List<ulong> targetPlayerIds = LocalSelfCoopContext.LocalPlayerIds
            .Take(LocalSelfCoopContext.DesiredLocalPlayerCount)
            .ToList();
        if (targetPlayerIds.Count <= 1)
        {
            return;
        }

        UnlockState unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
        SerializableUnlockState serializableUnlockState = unlockState.ToSerializable();

        int added = 0;
        foreach (ulong playerId in targetPlayerIds)
        {
            bool exists = lobby.Players.Any((player) => player.id == playerId);
            if (exists)
            {
                continue;
            }

            loopbackService.SetCurrentSenderId(playerId);
            _ = lobby.AddLocalHostPlayerInternal(serializableUnlockState, MaxLocalAscensionLevel);
            added++;
        }

        List<ulong> removablePlayerIds = LocalSelfCoopContext.LocalPlayerIds
            .Skip(targetPlayerIds.Count)
            .ToList();
        int removed = 0;
        foreach (ulong removableId in removablePlayerIds)
        {
            int playerIndex = lobby.Players.FindIndex((player) => player.id == removableId);
            if (playerIndex < 0)
            {
                continue;
            }

            LobbyPlayer removedPlayer = lobby.Players[playerIndex];
            lobby.Players.RemoveAt(playerIndex);
            lobby.InputSynchronizer.OnPlayerDisconnected(removedPlayer.id);
            screen.RemotePlayerDisconnected(removedPlayer);
            removed++;
        }

        bool readyChanged = false;
        for (int i = 0; i < lobby.Players.Count; i++)
        {
            LobbyPlayer player = lobby.Players[i];
            if (player.id == LocalSelfCoopContext.PrimaryPlayerId || player.isReady)
            {
                continue;
            }

            player.isReady = true;
            lobby.Players[i] = player;
            screen.PlayerChanged(player);
            readyChanged = true;
        }

        LocalSelfCoopContext.EnsureLobbySenderContext("custom-run-opened");
        LocalMultiControlLogger.Info(
            $"自定义模式大厅本地人数已同步: target={targetPlayerIds.Count}, actual={lobby.Players.Count}, added={added}, removed={removed}, readyChanged={readyChanged}");
    }

    private static void EnsureLobbyMaxCapacity(StartRunLobby lobby)
    {
        const int maxLocalPlayerCount = 12;
        if (lobby.MaxPlayers >= maxLocalPlayerCount)
        {
            return;
        }

        AccessTools.Field(typeof(StartRunLobby), "<MaxPlayers>k__BackingField")?.SetValue(lobby, maxLocalPlayerCount);
    }
}
