using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToBeginEnemyTurn))]
internal static class CombatManagerReadyEnemyTurnPatch
{
    [HarmonyPrefix]
    private static void Prefix(CombatManager __instance, Player player, Func<Task>? actionDuringEnemyTurn)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        CombatState? state = __instance.DebugOnlyGetState();
        if (state == null || state.CurrentSide != CombatSide.Player || state.Players.Count != 2)
        {
            return;
        }

        HashSet<Player>? readySet = AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn")?.GetValue(__instance) as HashSet<Player>;
        if (readySet == null)
        {
            return;
        }

        Player? otherPlayer = state.Players.FirstOrDefault((p) => p.NetId != player.NetId);
        if (otherPlayer == null || readySet.Contains(otherPlayer))
        {
            return;
        }

        readySet.Add(otherPlayer);
        LocalMultiControlLogger.Info($"本地双人模式自动补齐敌方回合就绪: {player.NetId} + {otherPlayer.NetId}");
    }

    [HarmonyPostfix]
    private static void Postfix(CombatManager __instance, Func<Task>? actionDuringEnemyTurn)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        CombatState? state = __instance.DebugOnlyGetState();
        if (state == null)
        {
            return;
        }

        HashSet<Player>? readySet = AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn")?.GetValue(__instance) as HashSet<Player>;
        if (readySet == null)
        {
            return;
        }

        if (!__instance.EndingPlayerTurnPhaseTwo && state.CurrentSide == CombatSide.Player && readySet.Count >= state.Players.Count)
        {
            MethodInfo? afterAllReadyMethod = AccessTools.Method(typeof(CombatManager), "AfterAllPlayersReadyToBeginEnemyTurn");
            if (afterAllReadyMethod?.Invoke(__instance, new object?[] { actionDuringEnemyTurn }) is Task task)
            {
                LocalMultiControlLogger.Info("检测到敌方回合未推进，触发本地兜底推进。");
                TaskHelper.RunSafely(task);
            }
        }
    }
}
