using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace LocalMultiControl.Scripts.Patch;

internal static class CardSelectWakuuAutoFallbackPatch
{
    private sealed class SelectorPatchState
    {
        public IDisposable? Scope { get; set; }
    }

    private static void TryPushVakuuSelector(Player player, ref SelectorPatchState __state, string source)
    {
        __state = new SelectorPatchState();
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (!LocalWakuuRelicRuntime.ShouldForceAutoChoice(player))
        {
            return;
        }

        if (CardSelectCmd.Selector != null)
        {
            return;
        }

        __state.Scope = CardSelectCmd.PushSelector(new VakuuCardSelector());
        LocalMultiControlLogger.Warn($"瓦库自动选牌兜底已注入选择器: player={player.NetId}, source={source}");
    }

    private static void DisposeSelector(SelectorPatchState? state, string source)
    {
        if (state?.Scope == null)
        {
            return;
        }

        state.Scope.Dispose();
        LocalMultiControlLogger.Info($"瓦库自动选牌兜底已释放选择器: source={source}");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid))]
    [HarmonyPrefix]
    private static void FromSimpleGridPrefix(Player player, ref SelectorPatchState __state)
    {
        TryPushVakuuSelector(player, ref __state, "FromSimpleGrid");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid))]
    [HarmonyPostfix]
    private static void FromSimpleGridPostfix(SelectorPatchState __state)
    {
        DisposeSelector(__state, "FromSimpleGrid");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid))]
    [HarmonyFinalizer]
    private static Exception? FromSimpleGridFinalizer(Exception? __exception, SelectorPatchState __state)
    {
        DisposeSelector(__state, "FromSimpleGrid-finalizer");
        return __exception;
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGridForRewards))]
    [HarmonyPrefix]
    private static void FromSimpleGridForRewardsPrefix(Player player, ref SelectorPatchState __state)
    {
        TryPushVakuuSelector(player, ref __state, "FromSimpleGridForRewards");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGridForRewards))]
    [HarmonyPostfix]
    private static void FromSimpleGridForRewardsPostfix(SelectorPatchState __state)
    {
        DisposeSelector(__state, "FromSimpleGridForRewards");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGridForRewards))]
    [HarmonyFinalizer]
    private static Exception? FromSimpleGridForRewardsFinalizer(Exception? __exception, SelectorPatchState __state)
    {
        DisposeSelector(__state, "FromSimpleGridForRewards-finalizer");
        return __exception;
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand))]
    [HarmonyPrefix]
    private static void FromHandPrefix(Player player, ref SelectorPatchState __state)
    {
        TryPushVakuuSelector(player, ref __state, "FromHand");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand))]
    [HarmonyPostfix]
    private static void FromHandPostfix(SelectorPatchState __state)
    {
        DisposeSelector(__state, "FromHand");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand))]
    [HarmonyFinalizer]
    private static Exception? FromHandFinalizer(Exception? __exception, SelectorPatchState __state)
    {
        DisposeSelector(__state, "FromHand-finalizer");
        return __exception;
    }
}
