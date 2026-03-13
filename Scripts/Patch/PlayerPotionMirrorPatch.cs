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
    private static bool _isMirroring;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.AddPotionInternal))]
    private static void PostfixAddPotion(Player __instance, PotionModel potion, PotionProcureResult __result)
    {
        if (_isMirroring || !LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode || !__result.success)
        {
            return;
        }

        Player? otherPlayer = __instance.RunState.Players.FirstOrDefault((candidate) => candidate.NetId != __instance.NetId);
        if (otherPlayer == null)
        {
            return;
        }

        int slotIndex = __instance.PotionSlots.ToList().FindIndex((candidate) => candidate == potion);
        if (slotIndex < 0)
        {
            return;
        }

        _isMirroring = true;
        try
        {
            PotionModel mirroredPotion = PotionModel.FromSerializable(potion.ToSerializable(slotIndex));
            otherPlayer.AddPotionInternal(mirroredPotion, slotIndex, silent: true);
            LocalMultiControlLogger.Info($"本地双人共享药水同步: {potion.Id.Entry}, {__instance.NetId} -> {otherPlayer.NetId}");
        }
        finally
        {
            _isMirroring = false;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.DiscardPotionInternal))]
    private static void PostfixDiscardPotion(Player __instance, PotionModel potion)
    {
        MirrorRemoval(__instance, potion, removeUsed: false);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.RemoveUsedPotionInternal))]
    private static void PostfixRemoveUsedPotion(Player __instance, PotionModel potion)
    {
        MirrorRemoval(__instance, potion, removeUsed: true);
    }

    private static void MirrorRemoval(Player owner, PotionModel potion, bool removeUsed)
    {
        if (_isMirroring || !LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        Player? otherPlayer = owner.RunState.Players.FirstOrDefault((candidate) => candidate.NetId != owner.NetId);
        PotionModel? mirroredPotion = otherPlayer?.Potions.FirstOrDefault((candidate) => candidate.Id == potion.Id);
        if (otherPlayer == null || mirroredPotion == null)
        {
            return;
        }

        _isMirroring = true;
        try
        {
            if (removeUsed)
            {
                otherPlayer.RemoveUsedPotionInternal(mirroredPotion);
            }
            else
            {
                otherPlayer.DiscardPotionInternal(mirroredPotion, silent: true);
            }

            LocalMultiControlLogger.Info($"本地双人共享药水移除同步: {potion.Id.Entry}, owner={otherPlayer.NetId}");
        }
        finally
        {
            _isMirroring = false;
        }
    }
}
