using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(Player), "PopulateStartingInventory")]
internal static class PlayerPotionSlotsPatch
{
    [HarmonyPostfix]
    private static void Postfix(Player __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        // 需求调整：不再额外扩展药水栏位。
    }
}

[HarmonyPatch]
internal static class PlayerPotionMirrorPatch
{
    internal static PotionModel ResolvePotionForUi(PotionModel potion)
    {
        return potion;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.AddPotionInternal))]
    private static void PostfixAddPotion(Player __instance, PotionModel potion, PotionProcureResult __result)
    {
        // 需求调整：药水按角色独立结算，不再重定向到1号位。
    }
}

[HarmonyPatch(typeof(PotionModel), nameof(PotionModel.EnqueueManualUse))]
internal static class PotionManualUseTargetPatch
{
    [HarmonyPrefix]
    private static void Prefix(PotionModel __instance, ref Creature? target)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode || target == null)
        {
            return;
        }

        if (__instance.TargetType != TargetType.Self || target != __instance.Owner.Creature)
        {
            return;
        }

        ulong controlledPlayerId = LocalContext.NetId ?? __instance.Owner.NetId;
        Player? controlledPlayer = __instance.Owner.RunState.GetPlayer(controlledPlayerId);
        if (controlledPlayer == null || controlledPlayer.Creature == null || controlledPlayer == __instance.Owner)
        {
            return;
        }

        target = controlledPlayer.Creature;
        LocalMultiControlLogger.Info($"药水默认目标已跟随当前控制角色: potion={__instance.Id.Entry}, owner={__instance.Owner.NetId}, target={controlledPlayer.NetId}");
    }
}
