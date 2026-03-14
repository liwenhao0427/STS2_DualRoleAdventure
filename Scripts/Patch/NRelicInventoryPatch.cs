using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NRelicInventory), nameof(NRelicInventory.AnimateRelic))]
internal static class NRelicInventoryPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NRelicInventory __instance, RelicModel relic, Godot.Vector2? startPosition = null, Godot.Vector2? startScale = null)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (!LocalContext.IsMine(relic))
        {
            return false;
        }

        List<NRelicInventoryHolder>? relicNodes = AccessTools.Field(typeof(NRelicInventory), "_relicNodes")?.GetValue(__instance) as List<NRelicInventoryHolder>;
        NRelicInventoryHolder? holder = relicNodes?.FirstOrDefault((node) => node.Relic.Model == relic);
        if (holder == null)
        {
            LocalMultiControlLogger.Warn($"跳过遗物动画：当前视图不存在遗物 {relic.Id.Entry}");
            return false;
        }

        TaskHelper.RunSafely(holder.PlayNewlyAcquiredAnimation(startPosition, startScale));
        return false;
    }
}
