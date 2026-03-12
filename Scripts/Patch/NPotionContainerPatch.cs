using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NPotionContainer), nameof(NPotionContainer.AnimatePotion))]
internal static class NPotionContainerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NPotionContainer), nameof(NPotionContainer.Initialize))]
    private static void PrefixInitialize(NPotionContainer __instance, IRunState _)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        try
        {
            List<NPotionHolder>? holders = AccessTools.Field(typeof(NPotionContainer), "_holders")?.GetValue(__instance) as List<NPotionHolder>;
            Godot.Control? holdersContainer = AccessTools.Field(typeof(NPotionContainer), "_potionHolders")?.GetValue(__instance) as Godot.Control;
            if (holders == null || holders.Count == 0 || holdersContainer == null)
            {
                return;
            }

            foreach (NPotionHolder holder in holders.ToList())
            {
                holder.GetParent()?.RemoveChild(holder);
                holder.QueueFreeSafely();
            }

            holders.Clear();
            AccessTools.Field(typeof(NPotionContainer), "_focusedHolder")?.SetValue(__instance, null);
            LocalMultiControlLogger.Info("切换角色时已重建药水栏槽位，避免旧角色药水残留。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"重建药水栏失败: {exception.Message}");
        }
    }

    [HarmonyPrefix]
    private static bool Prefix(NPotionContainer __instance, PotionModel potion, Godot.Vector2? startPosition = null)
    {
        if (!LocalContext.IsMine(potion))
        {
            return false;
        }

        List<NPotionHolder>? holders = AccessTools.Field(typeof(NPotionContainer), "_holders")?.GetValue(__instance) as List<NPotionHolder>;
        NPotionHolder? holder = holders?.FirstOrDefault((node) => node.Potion != null && node.Potion.Model == potion);
        if (holder?.Potion == null)
        {
            LocalMultiControlLogger.Warn($"跳过药水动画：当前视图不存在药水 {potion.Id.Entry}");
            return false;
        }

        TaskHelper.RunSafely(holder.Potion.PlayNewlyAcquiredAnimation(startPosition));
        return false;
    }
}
