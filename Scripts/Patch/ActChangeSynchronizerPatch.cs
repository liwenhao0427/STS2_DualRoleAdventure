using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(ActChangeSynchronizer), nameof(ActChangeSynchronizer.SetLocalPlayerReady))]
internal static class ActChangeSynchronizerPatch
{
    [HarmonyPostfix]
    private static void Postfix(ActChangeSynchronizer __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return;
        }

        RunState? runState = AccessTools.Field(typeof(ActChangeSynchronizer), "_runState")?.GetValue(__instance) as RunState;
        if (runState == null || runState.Players.Count != 2)
        {
            return;
        }

        ulong? localNetId = LocalContext.NetId;
        if (!localNetId.HasValue)
        {
            return;
        }

        Player? mirroredPlayer = runState.Players.FirstOrDefault((player) => player.NetId != localNetId.Value);
        if (mirroredPlayer == null)
        {
            return;
        }

        List<bool>? readyPlayers = AccessTools.Field(typeof(ActChangeSynchronizer), "_readyPlayers")?.GetValue(__instance) as List<bool>;
        if (readyPlayers != null)
        {
            int mirroredSlot = runState.GetPlayerSlotIndex(mirroredPlayer);
            if (mirroredSlot >= 0 && mirroredSlot < readyPlayers.Count && readyPlayers[mirroredSlot])
            {
                return;
            }
        }

        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new VoteToMoveToNextActAction(mirroredPlayer));
        LocalMultiControlLogger.Info($"本地双人自动补齐下一幕就绪: local={localNetId.Value}, mirrored={mirroredPlayer.NetId}");
    }
}
