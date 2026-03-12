using System;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Saves;
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
        LocalMultiControlLogger.Info("进入与自己联机流程（iter-03）。");
        LocalSelfCoopSaveTag.ClearCurrentProfile();
        SaveManager.Instance.DeleteCurrentMultiplayerRun();
        LocalMultiControlLogger.Info("已清理历史多人存档，避免旧格式校验干扰。");

        NSubmenuStack? stack = GetStack(submenu);
        if (stack == null)
        {
            LocalMultiControlLogger.Warn("无法打开角色选择：未找到 NSubmenuStack。");
            return;
        }

        ulong primaryPlayerId = LocalSelfCoopContext.ResolvePrimaryPlayerId();
        LocalSelfCoopSaveTag.MarkCurrentProfile(LocalSelfCoopContext.PrimaryPlayerId, LocalSelfCoopContext.SecondaryPlayerId);
        LocalLoopbackHostGameService netService = new LocalLoopbackHostGameService(primaryPlayerId);
        LocalSelfCoopContext.Enable(netService);

        NCharacterSelectScreen characterSelectScreen = stack.GetSubmenuType<NCharacterSelectScreen>();
        LocalSelfCoopContext.ActiveCharacterSelectScreen = characterSelectScreen;
        characterSelectScreen.InitializeMultiplayerAsHost(netService, 2);
        if (!LocalSelfCoopContext.BootstrapSecondPlayer(characterSelectScreen))
        {
            LocalMultiControlLogger.Warn("初始化2人本地队伍失败，已回退为单人联机入口行为。");
        }

        stack.Push(characterSelectScreen);
        LocalMultiControlLogger.Info("已跳转到2人本地队伍角色选择界面。");
    }

    private static NSubmenuStack? GetStack(NSubmenu submenu)
    {
        return AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(submenu) as NSubmenuStack;
    }
}
