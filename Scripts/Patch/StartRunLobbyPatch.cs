using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Unlocks;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(StartRunLobby), "BeginRun")]
internal static class StartRunLobbyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(StartRunLobby __instance, string seed, System.Collections.Generic.List<ModifierModel> modifiers)
    {
        if (__instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        LocalMultiControlLogger.Info("检测到本地回环 Lobby 开始开局，接管 BeginRun 逻辑。");

        MethodInfo? updatePreferredAscensionMethod = AccessTools.Method(typeof(StartRunLobby), "UpdatePreferredAscension");
        updatePreferredAscensionMethod?.Invoke(__instance, Array.Empty<object>());

        LobbyBeginRunMessage beginRunMessage = new LobbyBeginRunMessage
        {
            playersInLobby = __instance.Players,
            seed = seed,
            modifiers = modifiers.Select((modifier) => modifier.ToSerializable()).ToList(),
            act1 = __instance.Act1
        };
        __instance.NetService.SendMessage(beginRunMessage);

        UnlockState unlockState = GetUnlockState(__instance);
        System.Collections.Generic.List<ActModel> acts = ActModel.GetRandomList(seed, unlockState, __instance.NetService.Type.IsMultiplayer()).ToList();
        ActModel? act1Override = GetAct1(__instance.Act1);
        if (act1Override != null)
        {
            acts[0] = act1Override;
        }

        AccessTools.Field(typeof(StartRunLobby), "_beginningRun")?.SetValue(__instance, true);
        __instance.LobbyListener.BeginRun(seed, acts, modifiers);
        LocalMultiControlLogger.Info($"本地回环 Lobby 开局流程完成，玩家数={__instance.Players.Count}，seed={seed}");
        return false;
    }

    private static UnlockState GetUnlockState(StartRunLobby lobby)
    {
        MethodInfo? getUnlockStateMethod = AccessTools.Method(typeof(StartRunLobby), "GetUnlockState");
        UnlockState? unlockState = getUnlockStateMethod?.Invoke(lobby, Array.Empty<object>()) as UnlockState;
        if (unlockState == null)
        {
            throw new InvalidOperationException("无法从 StartRunLobby 获取 UnlockState。");
        }

        return unlockState;
    }

    private static ActModel? GetAct1(string act1Key)
    {
        MethodInfo? getActMethod = AccessTools.Method(typeof(StartRunLobby), "GetAct", new[] { typeof(string) });
        return getActMethod?.Invoke(null, new object[] { act1Key }) as ActModel;
    }
}
