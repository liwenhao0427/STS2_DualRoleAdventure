using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

internal static class GoldMirrorSuppressionContext
{
    private static readonly AsyncLocal<int> SuppressDepth = new();

    internal static bool ShouldSuppressGoldMirror => SuppressDepth.Value > 0;

    internal static async Task<T> RunSuppressedAsync<T>(Task<T> task)
    {
        SuppressDepth.Value++;
        try
        {
            return await task;
        }
        finally
        {
            if (SuppressDepth.Value > 0)
            {
                SuppressDepth.Value--;
            }
        }
    }
}

[HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.GainGold))]
internal static class PlayerGainGoldMirrorPatch
{
    private static readonly AsyncLocal<bool> IsMirroring = new();

    [HarmonyPostfix]
    private static void Postfix(decimal amount, Player player, bool wasStolenBack, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (amount <= 0m || IsMirroring.Value)
        {
            return;
        }

        if (GoldMirrorSuppressionContext.ShouldSuppressGoldMirror)
        {
            return;
        }

        __result = MirrorGoldToOtherPlayersAsync(amount, player, wasStolenBack, __result);
    }

    private static async Task MirrorGoldToOtherPlayersAsync(decimal amount, Player sourcePlayer, bool wasStolenBack, Task originalTask)
    {
        await originalTask;

        var otherPlayers = sourcePlayer.RunState.Players.Where((candidate) => candidate.NetId != sourcePlayer.NetId).ToList();
        if (otherPlayers.Count == 0)
        {
            return;
        }

        IsMirroring.Value = true;
        try
        {
            foreach (Player otherPlayer in otherPlayers)
            {
                await PlayerCmd.GainGold(amount, otherPlayer, wasStolenBack);
            }

            LocalMultiControlLogger.Info(
                $"事件/流程金币已同步到其余角色: amount={amount}, owner={sourcePlayer.NetId}, mirrored={string.Join(",", otherPlayers.Select((player) => player.NetId))}");
        }
        finally
        {
            IsMirroring.Value = false;
        }
    }
}
