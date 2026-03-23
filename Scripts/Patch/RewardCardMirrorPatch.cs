using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RewardSynchronizer), nameof(RewardSynchronizer.SyncLocalObtainedCard))]
internal static class RewardCardMirrorPatch
{
    [HarmonyPostfix]
    private static void Postfix(RewardSynchronizer __instance, CardModel _)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        Player? sourcePlayer = ResolveSourcePlayer(__instance);
        if (sourcePlayer == null || !CrystalSphereMirrorRuntime.IsInCrystalSphereEventContext(sourcePlayer))
        {
            return;
        }

        // 水晶球事件改为在 OfferCustom 阶段扩展“每位角色一组卡牌奖励”，
        // 这里不再做“选中后单张卡镜像”，避免重复加卡。
    }

    private static Player? ResolveSourcePlayer(RewardSynchronizer synchronizer)
    {
        ulong localPlayerId = AccessTools.Field(typeof(RewardSynchronizer), "_localPlayerId")?.GetValue(synchronizer) as ulong? ?? 0UL;
        if (localPlayerId == 0UL)
        {
            return null;
        }

        IPlayerCollection? playerCollection = AccessTools.Field(typeof(RewardSynchronizer), "_playerCollection")
            ?.GetValue(synchronizer) as IPlayerCollection;
        return playerCollection?.GetPlayer(localPlayerId);
    }
}
