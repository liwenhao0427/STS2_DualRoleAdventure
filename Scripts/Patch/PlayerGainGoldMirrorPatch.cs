using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

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

        __result = MirrorGoldToOtherPlayerAsync(amount, player, wasStolenBack, __result);
    }

    private static async Task MirrorGoldToOtherPlayerAsync(decimal amount, Player sourcePlayer, bool wasStolenBack, Task originalTask)
    {
        await originalTask;

        Player? otherPlayer = sourcePlayer.RunState.Players.FirstOrDefault((candidate) => candidate.NetId != sourcePlayer.NetId);
        if (otherPlayer == null)
        {
            return;
        }

        IsMirroring.Value = true;
        try
        {
            await PlayerCmd.GainGold(amount, otherPlayer, wasStolenBack);
            LocalMultiControlLogger.Info(
                $"事件/流程金币已同步到另一角色: amount={amount}, owner={sourcePlayer.NetId}, mirrored={otherPlayer.NetId}");
        }
        finally
        {
            IsMirroring.Value = false;
        }
    }
}
