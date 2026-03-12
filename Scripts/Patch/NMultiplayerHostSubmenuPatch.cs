using System;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu._Ready))]
internal static class NMultiplayerHostSubmenuPatch
{
    private const string LocalSelfCoopButtonName = "LocalSelfCoopButton";

    [HarmonyPostfix]
    private static void Postfix(NMultiplayerHostSubmenu __instance)
    {
        try
        {
            if (__instance.GetNodeOrNull<Button>(LocalSelfCoopButtonName) != null)
            {
                LocalMultiControlLogger.Info("联机菜单入口已存在，跳过重复注入。");
                return;
            }

            NSubmenuButton? standardButton = __instance.GetNodeOrNull<NSubmenuButton>("StandardButton");
            Control container = standardButton?.GetParent<Control>() ?? __instance;

            Button button = new Button
            {
                Name = LocalSelfCoopButtonName,
                Text = "与自己联机（2人本地）",
                FocusMode = Control.FocusModeEnum.All,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            if (standardButton != null)
            {
                button.SizeFlagsHorizontal = standardButton.SizeFlagsHorizontal;
                button.SizeFlagsVertical = standardButton.SizeFlagsVertical;
                button.CustomMinimumSize = standardButton.CustomMinimumSize;
            }

            container.AddChild(button);
            if (standardButton != null)
            {
                int targetIndex = Math.Min(standardButton.GetIndex() + 1, container.GetChildCount() - 1);
                container.MoveChild(button, targetIndex);
            }

            button.Pressed += () => OnLocalSelfCoopPressed(__instance);
            LocalMultiControlLogger.Info("联机菜单已注入“与自己联机”入口。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error($"注入“与自己联机”入口失败: {exception}");
        }
    }

    private static void OnLocalSelfCoopPressed(NMultiplayerHostSubmenu submenu)
    {
        LocalMultiControlLogger.Info("进入与自己联机流程（iter-02 占位实现）。");
        LocalMultiControlLogger.Info("初始化2人本地队伍（占位日志，iter-03 接入真实双角色运行）。");

        NSubmenuStack? stack = GetStack(submenu);
        if (stack == null)
        {
            LocalMultiControlLogger.Warn("无法打开角色选择：未找到 NSubmenuStack。");
            return;
        }

        NCharacterSelectScreen characterSelectScreen = stack.GetSubmenuType<NCharacterSelectScreen>();
        characterSelectScreen.InitializeSingleplayer();
        stack.Push(characterSelectScreen);
        LocalMultiControlLogger.Info("已跳转到角色选择界面（当前临时复用单人初始化路径）。");
    }

    private static NSubmenuStack? GetStack(NSubmenu submenu)
    {
        return AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(submenu) as NSubmenuStack;
    }
}
