using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Random;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked))]
internal static class TreasureRoomRelicSynchronizerPatch
{
    [HarmonyPostfix]
    private static void Postfix(TreasureRoomRelicSynchronizer __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        try
        {
            List<int?>? votes = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes")?.GetValue(__instance) as List<int?>;
            IReadOnlyList<MegaCrit.Sts2.Core.Models.RelicModel>? currentRelics = __instance.CurrentRelics;
            Rng? rng = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_rng")?.GetValue(__instance) as Rng;
            if (votes == null || currentRelics == null || currentRelics.Count == 0 || rng == null)
            {
                return;
            }

            bool hasPendingVote = false;
            for (int i = 0; i < votes.Count; i++)
            {
                if (!votes[i].HasValue)
                {
                    votes[i] = rng.NextInt(currentRelics.Count);
                    hasPendingVote = true;
                }
            }

            if (!hasPendingVote)
            {
                return;
            }

            AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), "AwardRelics")?.Invoke(__instance, new object[] { });
            AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), "EndRelicVoting")?.Invoke(__instance, new object[] { });
            LocalMultiControlLogger.Info("本地双人模式已自动补齐宝箱投票（随机），按简化随机宝箱流程结算。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"宝箱随机投票补齐失败: {exception.Message}");
        }
    }
}
