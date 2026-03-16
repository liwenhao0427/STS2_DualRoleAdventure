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

        bool isLocalSelfCoopPlayer = false;
        foreach (ulong localPlayerId in LocalSelfCoopContext.LocalPlayerIds)
        {
            if (localPlayerId != player.NetId)
            {
                continue;
            }

            isLocalSelfCoopPlayer = true;
            break;
        }

        if (!isLocalSelfCoopPlayer)
        {
            return true;
        }

        __result = true;
        LocalMultiControlLogger.Info(
            $"本地多控下强制本地处理选牌: player={player.NetId}, inCombat={CombatManager.Instance.IsInProgress}");
        return false;
    }
}
