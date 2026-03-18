using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(CardSelectCmd), "ShouldSelectLocalCard")]
internal static class CardSelectCmdPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        // 需求修正：不要强制本地自动选牌，避免事件/奖励出现随机代选。
        return true;
    }
}
