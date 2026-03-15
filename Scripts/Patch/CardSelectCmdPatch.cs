using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(CardSelectCmd), "ShouldSelectLocalCard")]
internal static class CardSelectCmdPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player player, ref bool __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        if (!CombatManager.Instance.IsInProgress)
        {
            // 战斗外（篝火/事件）必须保留手动选牌，避免被自动随机代选。
            return true;
        }

        __result = true;
        LocalMultiControlLogger.Info($"本地双人模式下强制本地处理选牌: player={player.NetId}, inCombat={CombatManager.Instance.IsInProgress}");
        return false;
    }
}
