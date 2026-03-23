using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

internal static class CardSelectForegroundSwitchPatch
{
    private static void EnsureForegroundForCombatChoice(Player player, string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (!RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        if (!LocalSelfCoopContext.LocalPlayerIds.Contains(player.NetId))
        {
            return;
        }

        ulong currentPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId
            ?? LocalContext.NetId
            ?? LocalSelfCoopContext.PrimaryPlayerId;
        if (currentPlayerId == player.NetId)
        {
            return;
        }

        LocalMultiControlLogger.Info(
            $"检测到战斗选牌请求来自后台角色，准备切换前台进行手选: source={source}, current={currentPlayerId}, target={player.NetId}");
        LocalMultiControlRuntime.SwitchControlledPlayerTo(player.NetId, $"combat-choice-{source}");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid))]
    [HarmonyPrefix]
    private static void FromSimpleGridPrefix(Player player)
    {
        EnsureForegroundForCombatChoice(player, "FromSimpleGrid");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand))]
    [HarmonyPrefix]
    private static void FromHandPrefix(Player player)
    {
        EnsureForegroundForCombatChoice(player, "FromHand");
    }
}
