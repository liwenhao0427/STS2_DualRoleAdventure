using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(WhisperingEarring), nameof(WhisperingEarring.BeforePlayPhaseStart))]
internal static class WhisperingEarringPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        WhisperingEarring __instance,
        PlayerChoiceContext choiceContext,
        Player player,
        ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || player != __instance.Owner)
        {
            return true;
        }

        __result = LocalWakuuRelicRuntime.ExecuteBeforePlayPhaseStartAsync(__instance, choiceContext, player);
        return false;
    }
}
