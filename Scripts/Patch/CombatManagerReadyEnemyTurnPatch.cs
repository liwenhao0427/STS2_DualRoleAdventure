using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToBeginEnemyTurn))]
internal static class CombatManagerReadyEnemyTurnPatch
{
    private static bool _isMirroring;

    [HarmonyPostfix]
    private static void Postfix(CombatManager __instance, Player player, Func<Task>? actionDuringEnemyTurn)
    {
        if (!LocalSelfCoopContext.IsEnabled || _isMirroring)
        {
            return;
        }

        CombatState? state = __instance.DebugOnlyGetState();
        if (state == null || state.CurrentSide != CombatSide.Player || state.Players.Count != 2)
        {
            return;
        }

        HashSet<Player>? readySet = AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn")?.GetValue(__instance) as HashSet<Player>;
        if (readySet == null || !readySet.Contains(player))
        {
            return;
        }

        Player? otherPlayer = state.Players.FirstOrDefault((p) => p.NetId != player.NetId);
        if (otherPlayer == null || readySet.Contains(otherPlayer))
        {
            return;
        }

        try
        {
            _isMirroring = true;
            __instance.SetReadyToBeginEnemyTurn(otherPlayer, actionDuringEnemyTurn);
            LocalMultiControlLogger.Info($"本地双人模式自动补齐敌方回合就绪: {player.NetId} + {otherPlayer.NetId}");
        }
        finally
        {
            _isMirroring = false;
        }
    }
}
