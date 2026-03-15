using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NRemoteLobbyPlayer), nameof(NRemoteLobbyPlayer._Ready))]
internal static class NRemoteLobbyPlayerReadyPatch
{
    [HarmonyPostfix]
    private static void Postfix(NRemoteLobbyPlayer __instance)
    {
        LocalRemoteLobbyPlayerSwitchUi.Ensure(__instance);
    }
}

internal static class LocalRemoteLobbyPlayerSwitchUi
{
    private const string ButtonName = "LocalLobbyPlayerSwitchButton";
    private const string WakuuToggleName = "LocalLobbyPlayerWakuuToggle";
    private const string WakuuHintName = "LocalLobbyPlayerWakuuHint";
    private const string WakuuHintText = "瓦库将接管你的回合";
    private const string TrackerName = "LocalLobbyPlayerSwitchTracker";

    public static void Ensure(NRemoteLobbyPlayer playerNode)
    {
        EnsureButton(playerNode);
        EnsureWakuuToggle(playerNode);
        EnsureWakuuHint(playerNode);
        EnsureTracker(playerNode);
        Refresh(playerNode);
    }

    public static void Refresh(NRemoteLobbyPlayer playerNode)
    {
        LocalSimpleTextButton? button = playerNode.GetNodeOrNull<LocalSimpleTextButton>(ButtonName);
        CheckButton? wakuuToggle = playerNode.GetNodeOrNull<CheckButton>(WakuuToggleName);
        Label? wakuuHint = playerNode.GetNodeOrNull<Label>(WakuuHintName);
        if (button == null || wakuuToggle == null || wakuuHint == null)
        {
            return;
        }

        bool inCharacterSelect = IsInsideCharacterSelect(playerNode);
        bool shouldShow = LocalSelfCoopContext.IsEnabled
                          && !RunManager.Instance.IsInProgress
                          && inCharacterSelect
                          && LocalSelfCoopContext.LocalPlayerIds.Contains(playerNode.PlayerId);
        button.Visible = shouldShow;
        wakuuToggle.Visible = shouldShow;
        wakuuHint.Visible = shouldShow;
        if (!shouldShow)
        {
            return;
        }

        button.ButtonText = string.Empty;

        // 右侧固定偏移，避免覆盖左侧玩家信息。
        float fixedX = playerNode.GlobalPosition.X + playerNode.Size.X + 72f;
        button.GlobalPosition = new Vector2(fixedX, playerNode.GlobalPosition.Y + 2f);

        bool wakuuEnabled = LocalSelfCoopContext.IsWakuuEnabled(playerNode.PlayerId);
        wakuuToggle.SetPressedNoSignal(wakuuEnabled);
        wakuuToggle.GlobalPosition = button.GlobalPosition + new Vector2(button.Size.X + 10f, -1f);
        wakuuHint.GlobalPosition = wakuuToggle.GlobalPosition + new Vector2(wakuuToggle.Size.X + 8f, 5f);
    }

    private static void EnsureButton(NRemoteLobbyPlayer playerNode)
    {
        if (playerNode.GetNodeOrNull<LocalSimpleTextButton>(ButtonName) != null)
        {
            return;
        }

        LocalSimpleTextButton button = new LocalSimpleTextButton
        {
            Name = ButtonName,
            ButtonText = string.Empty,
            FontSize = 18,
            FocusMode = Control.FocusModeEnum.None,
            Size = new Vector2(68f, 32f),
            CustomMinimumSize = new Vector2(68f, 32f),
            ImageScale = Vector2.One * 1.5f,
            TopLevel = true,
            ZIndex = 90
        };
        button.Connect(
            MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl.SignalName.Released,
            Callable.From<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>((_) =>
                LocalSelfCoopContext.SetLobbyEditingPlayer(playerNode.PlayerId, "char-select-avatar-button")));
        playerNode.AddChildSafely(button);
    }

    private static void EnsureWakuuToggle(NRemoteLobbyPlayer playerNode)
    {
        if (playerNode.GetNodeOrNull<CheckButton>(WakuuToggleName) != null)
        {
            return;
        }

        CheckButton toggle = new CheckButton
        {
            Name = WakuuToggleName,
            Text = string.Empty,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Size = new Vector2(38f, 34f),
            CustomMinimumSize = new Vector2(38f, 34f),
            TopLevel = true,
            ZIndex = 90
        };
        toggle.Connect(
            BaseButton.SignalName.Toggled,
            Callable.From<bool>((pressed) =>
                LocalSelfCoopContext.SetWakuuEnabled(playerNode.PlayerId, pressed, "char-select-left-list-toggle")));
        playerNode.AddChildSafely(toggle);
    }

    private static void EnsureWakuuHint(NRemoteLobbyPlayer playerNode)
    {
        if (playerNode.GetNodeOrNull<Label>(WakuuHintName) != null)
        {
            return;
        }

        Label hint = new Label
        {
            Name = WakuuHintName,
            Text = WakuuHintText,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TopLevel = true,
            ZIndex = 90
        };
        hint.AddThemeFontSizeOverride("font_size", 16);
        hint.AddThemeColorOverride("font_color", new Color("f3efe6"));
        hint.AddThemeColorOverride("font_outline_color", new Color("111111"));
        hint.AddThemeConstantOverride("outline_size", 5);
        playerNode.AddChildSafely(hint);
    }

    private static void EnsureTracker(NRemoteLobbyPlayer playerNode)
    {
        if (playerNode.GetNodeOrNull<LocalRemoteLobbyPlayerSwitchTracker>(TrackerName) != null)
        {
            return;
        }

        LocalRemoteLobbyPlayerSwitchTracker tracker = new LocalRemoteLobbyPlayerSwitchTracker
        {
            Name = TrackerName
        };
        tracker.Initialize(playerNode);
        playerNode.AddChild(tracker);
    }

    private static bool IsInsideCharacterSelect(Node node)
    {
        for (Node? current = node; current != null; current = current.GetParent())
        {
            if (current is NCharacterSelectScreen)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed partial class LocalRemoteLobbyPlayerSwitchTracker : Node
{
    private NRemoteLobbyPlayer? _playerNode;

    public void Initialize(NRemoteLobbyPlayer playerNode)
    {
        _playerNode = playerNode;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_playerNode == null || !GodotObject.IsInstanceValid(_playerNode))
        {
            QueueFree();
            return;
        }

        LocalRemoteLobbyPlayerSwitchUi.Refresh(_playerNode);
    }
}
