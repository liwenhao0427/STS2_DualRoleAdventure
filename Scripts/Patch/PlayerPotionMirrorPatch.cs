using System.Collections.Generic;
using System.Linq;
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

        __instance.AddToMaxPotionCount(2);
        LocalMultiControlLogger.Info($"玩家药水栏位已增加2格: player={__instance.NetId}, newCount={__instance.MaxPotionCount}");
    }
}

[HarmonyPatch]
internal static class PlayerPotionMirrorPatch
{
    private static readonly Dictionary<PotionModel, PotionModel> RedirectedPotions = new(ReferenceEqualityComparer.Instance);

    private static bool _isRedirecting;

    internal static PotionModel ResolvePotionForUi(PotionModel potion)
    {
        if (RedirectedPotions.Remove(potion, out PotionModel? redirectedPotion))
        {
            return redirectedPotion;
        }

        return potion;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.AddPotionInternal))]
    private static void PostfixAddPotion(Player __instance, PotionModel potion, PotionProcureResult __result)
    {
        if (_isRedirecting || !LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode || !__result.success)
        {
            return;
        }

        if (__instance.NetId == LocalSelfCoopContext.PrimaryPlayerId)
        {
            return;
        }

        Player? primaryPlayer = __instance.RunState.Players.FirstOrDefault((candidate) => candidate.NetId == LocalSelfCoopContext.PrimaryPlayerId);
        if (primaryPlayer == null)
        {
            return;
        }

        int slotIndex = __instance.PotionSlots.ToList().FindIndex((candidate) => candidate == potion);
        if (slotIndex < 0)
        {
            return;
        }

        _isRedirecting = true;
        try
        {
            __instance.DiscardPotionInternal(potion, silent: true);
            PotionModel redirectedPotion = PotionModel.FromSerializable(potion.ToSerializable(slotIndex));
            PotionProcureResult redirectResult = primaryPlayer.AddPotionInternal(redirectedPotion, -1, silent: true);
            if (!redirectResult.success)
            {
                LocalMultiControlLogger.Warn($"药水重定向到1号位失败（主角色已满）: {potion.Id.Entry}");
                return;
            }

            RedirectedPotions[potion] = redirectedPotion;
            LocalMultiControlLogger.Info($"药水已固定归属1号位: {potion.Id.Entry}, from={__instance.NetId}, to={primaryPlayer.NetId}");
        }
        finally
        {
            _isRedirecting = false;
        }
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
