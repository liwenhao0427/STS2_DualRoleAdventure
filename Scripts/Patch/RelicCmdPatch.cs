using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), new[] { typeof(RelicModel), typeof(Player), typeof(int) })]
internal static class RelicCmdObtainPatch
{
    private static readonly HashSet<RelicModel> NonSharedTreasureRelics = new HashSet<RelicModel>(ReferenceEqualityComparer.Instance);

    internal static bool TryConsumeNonSharedTreasureRelic(RelicModel relic)
    {
        return NonSharedTreasureRelics.Remove(relic);
    }

    [HarmonyPostfix]
    private static void Postfix(Player player, ref Task<RelicModel> __result)
    {
        __result = MirrorObtainForOtherLocalPlayerAsync(player, __result);
    }

    private static async Task<RelicModel> MirrorObtainForOtherLocalPlayerAsync(Player player, Task<RelicModel> originalTask)
    {
        RelicModel obtainedRelic = await originalTask;
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return obtainedRelic;
        }

        if (player.RunState == null || player.RunState.Players == null || player.RunState.Players.Count != 2)
        {
            return obtainedRelic;
        }

        if (player.RunState.CurrentRoom is TreasureRoom)
        {
            NonSharedTreasureRelics.Add(obtainedRelic);
            LocalMultiControlLogger.Info($"宝箱遗物按独立策略处理，不做共享镜像: relic={obtainedRelic.Id.Entry}, owner={player.NetId}");
            return obtainedRelic;
        }

        Player? otherPlayer = player.RunState.Players.FirstOrDefault((candidate) => candidate.NetId != player.NetId);
        if (otherPlayer == null)
        {
            return obtainedRelic;
        }

        if (!obtainedRelic.IsStackable && otherPlayer.GetRelicById(obtainedRelic.Id) != null)
        {
            return obtainedRelic;
        }

        try
        {
            RelicModel mirroredRelic = RelicModel.FromSerializable(obtainedRelic.ToSerializable());
            otherPlayer.AddRelicInternal(mirroredRelic);
            await mirroredRelic.AfterObtained();
            LocalMultiControlLogger.Info($"本地双人共享遗物同步: {obtainedRelic.Id.Entry}, {player.NetId} -> {otherPlayer.NetId}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"共享遗物同步失败(获得): {exception.Message}");
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
        __result = MirrorRemoveForOtherLocalPlayerAsync(relic, __result);
    }

    private static async Task MirrorRemoveForOtherLocalPlayerAsync(RelicModel removedRelic, Task originalTask)
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
        if (runState.Players.Count != 2)
        {
            return;
        }

        Player? otherPlayer = runState.Players.FirstOrDefault((candidate) => candidate.NetId != removedRelic.Owner.NetId);
        RelicModel? mirroredRelic = otherPlayer?.GetRelicById(removedRelic.Id);
        if (mirroredRelic == null)
        {
            return;
        }

        try
        {
            otherPlayer!.RemoveRelicInternal(mirroredRelic);
            await mirroredRelic.AfterRemoved();
            LocalMultiControlLogger.Info($"本地双人共享遗物同步移除: {removedRelic.Id.Entry}, owner={otherPlayer.NetId}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"共享遗物同步失败(移除): {exception.Message}");
        }
    }
}
