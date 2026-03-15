using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), new[] { typeof(RelicModel), typeof(Player), typeof(int) })]
internal static class RelicCmdObtainPatch
{
    private static readonly HashSet<RelicModel> NonSharedTreasureRelics = new(ReferenceEqualityComparer.Instance);

    internal static bool TryConsumeNonSharedTreasureRelic(RelicModel relic)
    {
        return NonSharedTreasureRelics.Remove(relic);
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        GoldMirrorSuppressionContext.EnterSuppression();
    }

    [HarmonyPostfix]
    private static void Postfix(Player player, ref Task<RelicModel> __result)
    {
        __result = GoldMirrorSuppressionContext.ExitSuppressionWhenCompleteAsync(MirrorObtainForOtherLocalPlayersAsync(player, __result));
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        if (__exception != null)
        {
            GoldMirrorSuppressionContext.ExitSuppressionOnce();
        }

        return __exception;
    }

    private static async Task<RelicModel> MirrorObtainForOtherLocalPlayersAsync(Player player, Task<RelicModel> originalTask)
    {
        RelicModel obtainedRelic = await originalTask;
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return obtainedRelic;
        }

        if (player.RunState?.Players == null || player.RunState.Players.Count <= 1)
        {
            return obtainedRelic;
        }

        if (player.RunState.CurrentRoom is TreasureRoom)
        {
            NonSharedTreasureRelics.Add(obtainedRelic);
            LocalMultiControlLogger.Info($"宝箱遗物按独立策略处理，不做共享镜像: relic={obtainedRelic.Id.Entry}, owner={player.NetId}");
            return obtainedRelic;
        }

        if (player.RunState.CurrentRoom is EventRoom)
        {
            LocalMultiControlLogger.Info($"事件遗物按独立策略处理，不做共享镜像: relic={obtainedRelic.Id.Entry}, owner={player.NetId}");
            return obtainedRelic;
        }

        foreach (Player otherPlayer in player.RunState.Players.Where((candidate) => candidate.NetId != player.NetId))
        {
            if (!obtainedRelic.IsStackable && otherPlayer.GetRelicById(obtainedRelic.Id) != null)
            {
                continue;
            }

            try
            {
                RelicModel mirroredRelic = RelicModel.FromSerializable(obtainedRelic.ToSerializable());
                otherPlayer.AddRelicInternal(mirroredRelic);
                await mirroredRelic.AfterObtained();
                LocalMultiControlLogger.Info($"本地多控共享遗物同步: {obtainedRelic.Id.Entry}, {player.NetId} -> {otherPlayer.NetId}");
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"共享遗物同步失败(获得): target={otherPlayer.NetId}, error={exception.Message}");
            }
        }

        return obtainedRelic;
    }
}

[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Remove))]
internal static class RelicCmdRemovePatch
{
    [HarmonyPostfix]
    private static void Postfix(RelicModel relic, ref Task __result)
    {
        __result = MirrorRemoveForOtherLocalPlayersAsync(relic, __result);
    }

    private static async Task MirrorRemoveForOtherLocalPlayersAsync(RelicModel removedRelic, Task originalTask)
    {
        await originalTask;
        if (RelicCmdObtainPatch.TryConsumeNonSharedTreasureRelic(removedRelic))
        {
            return;
        }

        if (!LocalSelfCoopContext.IsEnabled || removedRelic.Owner?.RunState == null)
        {
            return;
        }

        IRunState runState = removedRelic.Owner.RunState;
        if (runState.Players.Count <= 1)
        {
            return;
        }

        if (runState.CurrentRoom is EventRoom)
        {
            LocalMultiControlLogger.Info($"事件遗物移除按独立策略处理，不做共享镜像: relic={removedRelic.Id.Entry}, owner={removedRelic.Owner.NetId}");
            return;
        }

        foreach (Player otherPlayer in runState.Players.Where((candidate) => candidate.NetId != removedRelic.Owner.NetId))
        {
            RelicModel? mirroredRelic = otherPlayer.GetRelicById(removedRelic.Id);
            if (mirroredRelic == null)
            {
                continue;
            }

            try
            {
                otherPlayer.RemoveRelicInternal(mirroredRelic);
                await mirroredRelic.AfterRemoved();
                LocalMultiControlLogger.Info($"本地多控共享遗物同步移除: {removedRelic.Id.Entry}, owner={otherPlayer.NetId}");
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"共享遗物同步失败(移除): target={otherPlayer.NetId}, error={exception.Message}");
            }
        }
    }
}
