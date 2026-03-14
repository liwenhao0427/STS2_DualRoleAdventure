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
    private const string LabelName = "LocalSelfCoopCountLabel";

    public static void Sync(NCharacterSelectScreen screen)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            Remove(screen);
            return;
        }

        Control panel = EnsurePanel(screen);
        UpdateLayout(screen, panel);
        UpdateLabel(panel);
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

        LocalSimpleTextButton minusButton = CreateCountButton(MinusButtonName, "-");
        minusButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => OnAdjustPlayerCount(-1)));
        panel.AddChild(minusButton);

        LocalSimpleTextButton plusButton = CreateCountButton(PlusButtonName, "+");
        plusButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => OnAdjustPlayerCount(1)));
        panel.AddChild(plusButton);

        Label label = new Label
        {
            Name = LabelName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeColorOverride("font_color", new Color("fdf6e3"));
        label.AddThemeColorOverride("font_outline_color", new Color("1f1f1f"));
        label.AddThemeConstantOverride("outline_size", 8);
        label.AddThemeFontSizeOverride("font_size", 22);
        panel.AddChild(label);

        screen.AddChildSafely(panel);
        LocalMultiControlLogger.Info("角色选择页已创建本地人数 +/- 实体按钮。");
        return panel;
    }

    private static LocalSimpleTextButton CreateCountButton(string name, string text)
    {
        LocalSimpleTextButton button = new LocalSimpleTextButton
        {
            Name = name,
            ButtonText = text,
            FocusMode = Control.FocusModeEnum.None,
            Size = new Vector2(82f, 42f),
            CustomMinimumSize = new Vector2(82f, 42f)
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

        panel.Position = embarkButton.Position + new Vector2(-360f, 10f);

        if (panel.GetNodeOrNull<LocalSimpleTextButton>(MinusButtonName) is { } minusButton)
        {
            minusButton.Position = Vector2.Zero;
        }

        if (panel.GetNodeOrNull<LocalSimpleTextButton>(PlusButtonName) is { } plusButton)
        {
            plusButton.Position = new Vector2(92f, 0f);
        }

        if (panel.GetNodeOrNull<Label>(LabelName) is { } label)
        {
            label.Position = new Vector2(196f, 4f);
            label.Size = new Vector2(280f, 36f);
        }
    }

    private static void UpdateLabel(Control panel)
    {
        Label? label = panel.GetNodeOrNull<Label>(LabelName);
        if (label == null)
        {
            return;
        }

        int count = LocalSelfCoopContext.DesiredLocalPlayerCount;
        label.Text = $"本地人数: {count}  点击 +/- 调整";
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
}
