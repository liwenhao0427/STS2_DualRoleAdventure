using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom._Ready))]
internal static class NRestSiteRoomReadyGuardPatch
{
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        if (__exception is ArgumentOutOfRangeException)
        {
            LocalMultiControlLogger.Warn($"休息区初始化出现越界，已拦截并继续流程: {__exception.Message}");
            return null;
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(NTreasureRoomRelicCollection), "get_DefaultFocusedControl")]
internal static class NTreasureRoomRelicCollectionFocusGuardPatch
{
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        if (__exception is ArgumentOutOfRangeException)
        {
            LocalMultiControlLogger.Warn($"宝箱焦点控件越界，已拦截并跳过该帧聚焦: {__exception.Message}");
            return null;
        }

        return __exception;
    }
}
