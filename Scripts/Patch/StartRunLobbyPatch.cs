using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 1.01: BeginRun 已重命名为 BeginRunForAllPlayers，
/// 内部随机角色处理已移至 BeginRunLocally，直接委托即可。
/// </summary>
[HarmonyPatch(typeof(StartRunLobby), "BeginRunForAllPlayers")]
internal static class StartRunLobbyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(StartRunLobby __instance, string seed, List<ModifierModel> modifiers)
    {
        if (__instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        LocalMultiControlLogger.Info("检测到本地回环 Lobby 开始开局，接管 BeginRunForAllPlayers 逻辑。");

        MethodInfo? updatePreferredAscensionMethod = AccessTools.Method(typeof(StartRunLobby), "UpdatePreferredAscension");
        updatePreferredAscensionMethod?.Invoke(__instance, Array.Empty<object>());

        LobbyBeginRunMessage beginRunMessage = new()
        {
            playersInLobby = __instance.Players,
            seed = seed,
            modifiers = modifiers.Select(m => m.ToSerializable()).ToList(),
            act1 = __instance.Act1
        };
        __instance.NetService.SendMessage(beginRunMessage);

        // 1.01: BeginRunLocally 内部处理随机角色、Rng、单人 Ascension 等
        MethodInfo? beginRunLocally = AccessTools.Method(typeof(StartRunLobby), "BeginRunLocally");
        beginRunLocally?.Invoke(__instance, new object[] { seed, modifiers });

        LocalMultiControlLogger.Info($"本地回环 Lobby 开局流程完成，玩家数={__instance.Players.Count}，seed={seed}");
        return false;
    }
}
