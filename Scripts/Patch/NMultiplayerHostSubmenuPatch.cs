using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;

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
            if (__instance.GetNodeOrNull<NSubmenuButton>(LocalSelfCoopButtonName) != null)
            {
                LocalMultiControlLogger.Info("联机菜单入口已存在，跳过重复注入。");
                return;
            }

            NSubmenuButton? standardButton = __instance.GetNodeOrNull<NSubmenuButton>("StandardButton");
            NSubmenuButton? dailyButton = __instance.GetNodeOrNull<NSubmenuButton>("DailyButton");
            NSubmenuButton? customButton = __instance.GetNodeOrNull<NSubmenuButton>("CustomRunButton");
            if (standardButton == null)
            {
                LocalMultiControlLogger.Warn("未找到 StandardButton，无法注入本地双角色入口。");
                return;
            }

            NSubmenuButton templateButton = customButton ?? standardButton;
            NSubmenuButton button = CreateStyledButton(templateButton);
            button.Name = LocalSelfCoopButtonName;
            ApplyButtonText(button);
            button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>((_) => OnLocalSelfCoopPressed(__instance)));

            Control container = templateButton.GetParent<Control>();
            container.AddChild(button);
            int targetIndex = Math.Min(templateButton.GetIndex() + 1, container.GetChildCount() - 1);
            container.MoveChild(button, targetIndex);

            ArrangeFourButtonsHorizontally(standardButton, dailyButton, customButton, button);
            LocalMultiControlLogger.Info("联机菜单已注入卡片样式入口：单人双角色（四卡并列）。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error($"注入“单人双角色”入口失败: {exception}");
        }
    }

    private static NSubmenuButton CreateStyledButton(NSubmenuButton templateButton)
    {
        const Node.DuplicateFlags duplicateFlags = Node.DuplicateFlags.Groups |
                                                   Node.DuplicateFlags.Scripts |
                                                   Node.DuplicateFlags.UseInstantiation;
        return templateButton.Duplicate((int)duplicateFlags) as NSubmenuButton
            ?? throw new InvalidOperationException("复制模板按钮失败。");
    }

    private static void ApplyButtonText(NSubmenuButton button)
    {
        Node? titleNode = button.FindChild("Title", recursive: true, owned: false);
        if (titleNode is Label title)
        {
            title.Text = "单人双角色";
        }

        Node? descriptionNode = button.FindChild("Description", recursive: true, owned: false);
        if (descriptionNode is RichTextLabel description)
        {
            description.Text = "在本机创建两名可切换角色，进行本地双人协作。";
        }
    }

    private static void ArrangeFourButtonsHorizontally(
        NSubmenuButton standardButton,
        NSubmenuButton? dailyButton,
        NSubmenuButton? customButton,
        NSubmenuButton localButton)
    {
        if (dailyButton == null || customButton == null)
        {
            return;
        }

        List<NSubmenuButton> originalButtons = new() { standardButton, dailyButton, customButton };
        originalButtons = originalButtons.OrderBy((item) => item.Position.X).ToList();

        float minX = originalButtons[0].Position.X;
        float maxX = originalButtons[2].Position.X;
        if (Math.Abs(maxX - minX) < 1f)
        {
            return;
        }

        float y = originalButtons[0].Position.Y;
        float stepX = (maxX - minX) / 3f;
        List<NSubmenuButton> arranged = new() { localButton, originalButtons[0], originalButtons[1], originalButtons[2] };
        for (int index = 0; index < arranged.Count; index++)
        {
            NSubmenuButton button = arranged[index];
            button.Position = new Vector2(minX + stepX * index, y);
        }
    }

    private static void OnLocalSelfCoopPressed(NMultiplayerHostSubmenu submenu)
    {
        LocalMultiControlLogger.Info("进入单人双角色流程。");
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
            LocalMultiControlLogger.Warn("初始化本地双角色队伍失败，已回退到单入口行为。");
        }

        stack.Push(characterSelectScreen);
        LocalMultiControlLogger.Info("已跳转到双角色本地队伍角色选择界面。");
    }

    private static NSubmenuStack? GetStack(NSubmenu submenu)
    {
        return AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(submenu) as NSubmenuStack;
    }
}
