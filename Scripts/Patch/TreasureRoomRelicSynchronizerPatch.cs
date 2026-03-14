using System;
using System.Collections.Generic;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked))]
internal static class TreasureRoomRelicSynchronizerPatch
{
    [HarmonyPrefix]
    private static void Prefix(TreasureRoomRelicSynchronizer __instance, Player player)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        try
        {
            List<int?>? votes = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes")?.GetValue(__instance) as List<int?>;
            IReadOnlyList<MegaCrit.Sts2.Core.Models.RelicModel>? currentRelics = __instance.CurrentRelics;
            Rng? rng = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_rng")?.GetValue(__instance) as Rng;
            IPlayerCollection? players = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection")?.GetValue(__instance) as IPlayerCollection;
            if (votes == null || currentRelics == null || currentRelics.Count == 0 || rng == null || players == null)
            {
                return;
            }

            int pickedPlayerSlot = players.GetPlayerSlotIndex(player);
            bool hasPendingVote = false;
            for (int i = 0; i < votes.Count; i++)
            {
                if (i == pickedPlayerSlot || votes[i].HasValue)
                {
                    continue;
                }

                votes[i] = rng.NextInt(currentRelics.Count);
                hasPendingVote = true;
            }

            if (hasPendingVote)
            {
                LocalMultiControlLogger.Info("本地双人模式已自动补齐宝箱投票（随机），交由原始流程继续结算。");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"宝箱随机投票补齐失败: {exception.Message}");
        }
    }
}
