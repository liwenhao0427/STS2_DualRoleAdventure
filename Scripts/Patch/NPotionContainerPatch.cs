using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Potions;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NPotionContainer), nameof(NPotionContainer.AnimatePotion))]
internal static class NPotionContainerPatch
{
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
