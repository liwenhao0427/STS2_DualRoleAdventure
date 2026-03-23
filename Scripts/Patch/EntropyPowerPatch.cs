using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(EntropyPower), nameof(EntropyPower.AfterPlayerTurnStart))]
internal static class EntropyPowerPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EntropyPower __instance, PlayerChoiceContext choiceContext, Player player, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress)
        {
            return true;
        }

        if (player != __instance.Owner?.Player)
        {
            return true;
        }

        ulong currentControlledPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId
            ?? player.NetId;
        if (currentControlledPlayerId == player.NetId)
        {
            return true;
        }

        LocalDeferredTurnStartRuntime.QueueEntropyChoiceAndSwitchToOwner(__instance, player, "after-player-turn-start");
        __result = Task.CompletedTask;
        return false;
    }
}
