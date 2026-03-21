using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(PlatformUtil), nameof(PlatformUtil.GetPlayerName))]
internal static class PlatformUtilGetPlayerNamePatch
{
    [HarmonyPostfix]
    private static void Postfix(ulong playerId, ref string __result)
    {
        if (!ShouldOverridePlayerName())
        {
            return;
        }

        if (!LocalSelfCoopContext.TryGetSlotIndex(playerId, out int slotIndex))
        {
            return;
        }

        __result = LocalModText.RoleSlot((slotIndex + 1).ToString());
    }

    private static bool ShouldOverridePlayerName()
    {
        if (LocalSelfCoopContext.IsEnabled || LocalSelfCoopContext.ActiveCharacterSelectScreen != null)
        {
            return true;
        }

        if (RunManager.Instance == null || !RunManager.Instance.IsInProgress)
        {
            return false;
        }

        return LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId.HasValue;
    }
}
