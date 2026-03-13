using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch]
internal static class PlayerPotionMirrorPatch
{
    private static bool _isRedirecting;

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

            LocalMultiControlLogger.Info($"药水已固定归属1号位: {potion.Id.Entry}, from={__instance.NetId}, to={primaryPlayer.NetId}");
        }
        finally
        {
            _isRedirecting = false;
        }
    }
}
