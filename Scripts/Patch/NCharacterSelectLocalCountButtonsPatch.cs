using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
internal static class NCharacterSelectLocalCountButtonsOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        LocalCharacterSelectCountButtons.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Process))]
internal static class NCharacterSelectLocalCountButtonsProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        LocalCharacterSelectCountButtons.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuClosed))]
internal static class NCharacterSelectLocalCountButtonsClosePatch
{
    [HarmonyPrefix]
    private static void Prefix(NCharacterSelectScreen __instance)
    {
        LocalCharacterSelectCountButtons.Remove(__instance);
    }
}

internal static class LocalCharacterSelectCountButtons
{
    private const string PanelName = "LocalSelfCoopCountPanel";
    private const string MinusButtonName = "LocalSelfCoopMinusButton";
    private const string PlusButtonName = "LocalSelfCoopPlusButton";
    private const string PrevButtonName = "LocalSelfCoopPrevButton";
    private const string NextButtonName = "LocalSelfCoopNextButton";

    public static void Sync(NCharacterSelectScreen screen)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            Remove(screen);
            return;
        }

        Control panel = EnsurePanel(screen);
        UpdateLayout(screen, panel);
    }

    public static void Remove(NCharacterSelectScreen screen)
    {
        Control? existingPanel = screen.GetNodeOrNull<Control>(PanelName);
        existingPanel?.QueueFreeSafely();
    }

    private static Control EnsurePanel(NCharacterSelectScreen screen)
    {
        Control? existingPanel = screen.GetNodeOrNull<Control>(PanelName);
        if (existingPanel != null)
        {
            return existingPanel;
        }

        Control panel = new Control
        {
            Name = PanelName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 80
        };

        LocalSimpleTextButton minusButton = CreateCountButton(MinusButtonName, "-", false);
        minusButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => OnAdjustPlayerCount(-1)));
        panel.AddChild(minusButton);

        LocalSimpleTextButton plusButton = CreateCountButton(PlusButtonName, "+", true);
        plusButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => OnAdjustPlayerCount(1)));
        panel.AddChild(plusButton);

        LocalSimpleTextButton prevButton = CreateCountButton(PrevButtonName, string.Empty, false);
        prevButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => OnSwitchLobbyPlayer(false)));
        panel.AddChild(prevButton);

        LocalSimpleTextButton nextButton = CreateCountButton(NextButtonName, string.Empty, true);
        nextButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => OnSwitchLobbyPlayer(true)));
        panel.AddChild(nextButton);

        screen.AddChildSafely(panel);
        LocalMultiControlLogger.Info("角色选择页已创建本地人数 +/- 实体按钮。");
        return panel;
    }

    private static LocalSimpleTextButton CreateCountButton(string name, string text, bool mirrorImageX)
    {
        LocalSimpleTextButton button = new LocalSimpleTextButton
        {
            Name = name,
            ButtonText = text,
            FocusMode = Control.FocusModeEnum.None,
            FontSize = 20,
            Size = new Vector2(140f, 32f),
            CustomMinimumSize = new Vector2(140f, 32f),
            ImageScale = Vector2.One * 1.5f,
            MirrorImageX = mirrorImageX
        };
        return button;
    }

    private static void UpdateLayout(NCharacterSelectScreen screen, Control panel)
    {
        NConfirmButton? embarkButton =
            AccessTools.Field(typeof(NCharacterSelectScreen), "_embarkButton")?.GetValue(screen) as NConfirmButton;
        if (embarkButton == null)
        {
            return;
        }

        // 注意：该坐标经过实机对齐，目的是避免与确认按钮重叠导致 + 按钮不可点击。
        // 请不要随意改回靠右布局，如需改动先实测“+ 按钮在 2->3/4 人时可稳定点击”。
        panel.Position = embarkButton.Position + new Vector2(-296f, 94f);

        if (panel.GetNodeOrNull<LocalSimpleTextButton>(MinusButtonName) is { } minusButton)
        {
            minusButton.Position = Vector2.Zero;
        }

        if (panel.GetNodeOrNull<LocalSimpleTextButton>(PlusButtonName) is { } plusButton)
        {
            plusButton.Position = new Vector2(148f, 0f);
        }

        if (panel.GetNodeOrNull<LocalSimpleTextButton>(PrevButtonName) is { } prevButton)
        {
            prevButton.Position = new Vector2(0f, 40f);
        }

        if (panel.GetNodeOrNull<LocalSimpleTextButton>(NextButtonName) is { } nextButton)
        {
            nextButton.Position = new Vector2(148f, 40f);
        }
    }

    private static void OnAdjustPlayerCount(int delta)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        string source = delta > 0 ? "ui-button:+" : "ui-button:-";
        if (!LocalSelfCoopContext.AdjustDesiredLocalPlayerCount(delta, source))
        {
            return;
        }

        int targetCount = LocalSelfCoopContext.DesiredLocalPlayerCount;
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create($"本地人数: {targetCount}"));
        LocalMultiControlLogger.Info($"通过实体按钮调整本地人数成功: {targetCount}");

        NCharacterSelectScreen? activeScreen = LocalSelfCoopContext.ActiveCharacterSelectScreen;
        if (activeScreen != null && GodotObject.IsInstanceValid(activeScreen))
        {
            Sync(activeScreen);
        }
    }

    private static void OnSwitchLobbyPlayer(bool next)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        LocalSelfCoopContext.SwitchLobbyEditingPlayer(next);
    }
}
