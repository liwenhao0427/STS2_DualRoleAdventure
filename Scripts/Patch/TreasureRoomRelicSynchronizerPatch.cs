using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked))]
internal static class TreasureRoomRelicSynchronizerPatch
{
    [HarmonyPostfix]
    private static void Postfix(TreasureRoomRelicSynchronizer __instance, Player player)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        TryAutoSwitchToNextUnpickedPlayer(__instance, player.NetId, "treasure-picked");
    }

    internal static bool TryAutoSwitchToNextUnpickedPlayer(TreasureRoomRelicSynchronizer synchronizer, ulong currentPlayerId, string source)
    {
        try
        {
            List<int?>? votes = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes")?.GetValue(synchronizer) as List<int?>;
            IPlayerCollection? players = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection")?.GetValue(synchronizer) as IPlayerCollection;
            if (votes == null || players == null)
            {
                return false;
            }

            int sharedCount = Math.Min(votes.Count, players.Players.Count);
            if (sharedCount < 2)
            {
                return false;
            }

            int currentSlot = players.Players.Take(sharedCount).ToList().FindIndex((candidate) => candidate.NetId == currentPlayerId);
            if (currentSlot < 0)
            {
                return false;
            }

            if (!votes[currentSlot].HasValue)
            {
                return false;
            }

            int nextSlot = -1;
            for (int step = 1; step < sharedCount; step++)
            {
                int slot = (currentSlot + step) % sharedCount;
                if (!votes[slot].HasValue)
                {
                    nextSlot = slot;
                    break;
                }
            }

            if (nextSlot < 0)
            {
                return false;
            }

            ulong nextPlayerId = players.Players[nextSlot].NetId;
            Callable.From(delegate
            {
                LocalMultiControlRuntime.SwitchControlledPlayerTo(nextPlayerId, source);
            }).CallDeferred();

            LocalMultiControlLogger.Info($"宝箱选择完成后自动切换到下一位未选角色: {currentPlayerId} -> {nextPlayerId}");
            return true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"宝箱自动切换未选角色失败: {exception.Message}");
            return false;
        }
    }
}

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
internal static class TreasureRoomRelicSynchronizerBeginPatch
{
    [HarmonyPostfix]
    private static void Postfix(TreasureRoomRelicSynchronizer __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        try
        {
            List<int?>? votes = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes")?.GetValue(__instance) as List<int?>;
            if (votes == null || votes.Count <= 1)
            {
                return;
            }

            for (int i = 0; i < votes.Count; i++)
            {
                votes[i] = null;
            }

            LocalMultiControlLogger.Info("宝箱已禁用自动代投，改为逐角色手动选择。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"禁用宝箱自动代投失败: {exception.Message}");
        }
    }
}
