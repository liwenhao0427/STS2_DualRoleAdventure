using System;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Generate))]
internal static class RestSiteOptionPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref System.Collections.Generic.List<RestSiteOption> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        __result.RemoveAll((option) => option is MendRestSiteOption);
    }
}

[HarmonyPatch(typeof(HealRestSiteOption), nameof(HealRestSiteOption.OnSelect))]
internal static class HealRestSiteOptionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(HealRestSiteOption __instance, ref Task<bool> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        __result = HealAllPlayersAsync(__instance);
        return false;
    }

    private static async Task<bool> HealAllPlayersAsync(HealRestSiteOption option)
    {
        Player? owner = AccessTools.Field(typeof(RestSiteOption), "<Owner>k__BackingField")?.GetValue(option) as Player;
        if (owner == null)
        {
            return false;
        }

        if (owner.RunState == null || owner.RunState.Players.Count <= 1)
        {
            await HealRestSiteOption.ExecuteRestSiteHeal(owner, isMimicked: false);
            return true;
        }

        await HealRestSiteOption.ExecuteRestSiteHeal(owner, isMimicked: false);
        foreach (Player player in owner.RunState.Players)
        {
            if (player.NetId == owner.NetId)
            {
                continue;
            }

            await CreatureCmd.Heal(player.Creature, HealRestSiteOption.GetHealAmount(player));
            await Hook.AfterRestSiteHeal(player.RunState, player, isMimicked: true);
        }

        LocalMultiControlLogger.Info("休息区恢复已改为全体恢复。");
        return true;
    }
}
