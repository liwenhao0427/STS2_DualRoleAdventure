using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(CardSelectCmd), "ShouldSelectLocalCard")]
internal static class CardSelectCmdPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player player, ref bool __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleEventFlow)
        {
            return true;
        }

        if (CombatManager.Instance.IsInProgress)
        {
            return true;
        }

        // 风险点：单事件流下若走“等待远端选择”，非共享事件会卡在 Awaiting remote choice。
        // 这里统一改为本地处理非战斗选牌（附魔/升级/改造等），后续改动需回归所有选牌事件。
        __result = true;
        LocalMultiControlLogger.Info($"单事件流下改为本地处理非战斗选牌: player={player.NetId}");
        return false;
    }
}
