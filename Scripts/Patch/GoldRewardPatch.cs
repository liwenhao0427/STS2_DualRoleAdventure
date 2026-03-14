using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(GoldReward), "OnSelect")]
internal static class GoldRewardPatch
{
    [HarmonyPostfix]
    private static void Postfix(GoldReward __instance, ref Task<bool> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        __result = MirrorGoldToOtherPlayerAsync(__instance, __result);
    }

    private static async Task<bool> MirrorGoldToOtherPlayerAsync(GoldReward reward, Task<bool> originalTask)
    {
        bool selected = await originalTask;
        if (!selected)
        {
            return false;
        }

        Player? otherPlayer = reward.Player.RunState.Players.FirstOrDefault((player) => player.NetId != reward.Player.NetId);
        if (otherPlayer == null)
        {
            return true;
        }

        bool wasStolenBack = (bool)(AccessTools.Field(typeof(GoldReward), "_wasGoldStolenBack")?.GetValue(reward) ?? false);
        await PlayerCmd.GainGold(reward.Amount, otherPlayer, wasStolenBack);
        LocalMultiControlLogger.Info($"金币奖励已镜像到另一名角色: amount={reward.Amount}, owner={reward.Player.NetId}, mirrored={otherPlayer.NetId}");
        return true;
    }
}
