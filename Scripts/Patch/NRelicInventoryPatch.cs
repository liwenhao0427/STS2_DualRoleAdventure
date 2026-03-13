using System;
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
    [HarmonyPatch(typeof(NRelicInventory), nameof(NRelicInventory.Initialize))]
    private static void PrefixInitialize(NRelicInventory __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        try
        {
            List<NRelicInventoryHolder>? relicNodes = AccessTools.Field(typeof(NRelicInventory), "_relicNodes")?.GetValue(__instance) as List<NRelicInventoryHolder>;
            if (relicNodes == null || relicNodes.Count == 0)
            {
                return;
            }

            foreach (NRelicInventoryHolder relicNode in relicNodes.ToList())
            {
                relicNode.GetParent()?.RemoveChild(relicNode);
                relicNode.QueueFreeSafely();
            }

            relicNodes.Clear();
            LocalMultiControlLogger.Info("切换角色时已清空遗物栏旧节点，避免叠加显示。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"清空遗物栏旧节点失败: {exception.Message}");
        }
    }

    [HarmonyPrefix]
    private static bool Prefix(NRelicInventory __instance, RelicModel relic, Godot.Vector2? startPosition = null, Godot.Vector2? startScale = null)
    {
        if (LocalSelfCoopContext.UseSingleAdventureMode)
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
