using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using LocalMultiControl.Scripts.Runtime;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCharacterSelectButton), nameof(NCharacterSelectButton.Select))]
internal static class NCharacterSelectButtonPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NCharacterSelectButton __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return true;
        }

        try
        {
            bool isSelected = AccessTools.Field(typeof(NCharacterSelectButton), "_isSelected")?.GetValue(__instance) as bool? ?? false;
            if (!isSelected)
            {
                // 正常路径保持原逻辑，避免改变原生行为。
                return true;
            }

            ICharacterSelectButtonDelegate? selectDelegate =
                AccessTools.Field(typeof(NCharacterSelectButton), "_delegate")?.GetValue(__instance) as ICharacterSelectButtonDelegate;
            if (selectDelegate == null)
            {
                return true;
            }

            CharacterModel character = __instance.Character;
            selectDelegate.SelectCharacter(__instance, character);
            AccessTools.Method(typeof(NCharacterSelectButton), "RefreshState")?.Invoke(__instance, Array.Empty<object>());
            LocalMultiControlLogger.Info($"允许重复选角重提交流程: character={character.Id.Entry}");
            return false;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"重复选角补丁执行失败，回退原逻辑: {exception.Message}");
            return true;
        }
    }
}
