using System;
using System.Collections.Generic;
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
    private const string TrackerName = "LocalLobbyPlayerSwitchTracker";

    private static readonly Vector2 SelectorButtonSize = new(52f, 28f);
    private static readonly Vector2 WakuuToggleSize = new(30f, 28f);
    private static readonly Vector2 AnchorFallbackOffset = new(128f, 3f);

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
        wakuuHint.Visible = false;

        if (!shouldShow)
        {
            RestoreOriginalIdLabel(playerNode);
            return;
        }

        button.ButtonText = string.Empty;
        Rect2 anchorRect = ResolveIdAnchorRect(playerNode);
        HideOriginalIdLabel(playerNode);

        button.GlobalPosition = anchorRect.Position + new Vector2(0f, -1f);
        button.Size = SelectorButtonSize;
        button.CustomMinimumSize = SelectorButtonSize;

        bool wakuuEnabled = LocalSelfCoopContext.IsWakuuEnabled(playerNode.PlayerId);
        wakuuToggle.SetPressedNoSignal(wakuuEnabled);
        wakuuToggle.GlobalPosition = button.GlobalPosition + new Vector2(button.Size.X + 4f, 0f);
        wakuuToggle.Size = WakuuToggleSize;
        wakuuToggle.CustomMinimumSize = WakuuToggleSize;
    }

    private static void EnsureButton(NRemoteLobbyPlayer playerNode)
    {
        if (playerNode.GetNodeOrNull<LocalSimpleTextButton>(ButtonName) != null)
        {
            return;
        }

        LocalSimpleTextButton button = new()
        {
            Name = ButtonName,
            ButtonText = string.Empty,
            FontSize = 18,
            FocusMode = Control.FocusModeEnum.None,
            Size = SelectorButtonSize,
            CustomMinimumSize = SelectorButtonSize,
            ImageScale = Vector2.One * 1.5f,
            TopLevel = true,
            ZIndex = 90
        };

        button.Connect(
            MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl.SignalName.Released,
            Callable.From<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>((_) =>
                LocalSelfCoopContext.SetLobbyEditingPlayer(playerNode.PlayerId, "char-select-id-anchor-button")));

        playerNode.AddChildSafely(button);
    }

    private static void EnsureWakuuToggle(NRemoteLobbyPlayer playerNode)
    {
        if (playerNode.GetNodeOrNull<CheckButton>(WakuuToggleName) != null)
        {
            return;
        }

        CheckButton toggle = new()
        {
            Name = WakuuToggleName,
            Text = string.Empty,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Size = WakuuToggleSize,
            CustomMinimumSize = WakuuToggleSize,
            TopLevel = true,
            ZIndex = 90
        };

        toggle.Connect(
            BaseButton.SignalName.Toggled,
            Callable.From<bool>((pressed) =>
                LocalSelfCoopContext.SetWakuuEnabled(playerNode.PlayerId, pressed, "char-select-id-anchor-toggle")));

        playerNode.AddChildSafely(toggle);
    }

    private static void EnsureWakuuHint(NRemoteLobbyPlayer playerNode)
    {
        if (playerNode.GetNodeOrNull<Label>(WakuuHintName) != null)
        {
            return;
        }

        Label hint = new()
        {
            Name = WakuuHintName,
            Text = string.Empty,
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

        LocalRemoteLobbyPlayerSwitchTracker tracker = new()
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

    private static Rect2 ResolveIdAnchorRect(NRemoteLobbyPlayer playerNode)
    {
        Label? idLabel = TryFindIdLabel(playerNode);
        if (idLabel != null)
        {
            Vector2 size = idLabel.Size;
            if (size.X <= 2f)
            {
                size = new Vector2(92f, SelectorButtonSize.Y);
            }

            return new Rect2(idLabel.GlobalPosition, size);
        }

        Vector2 fallbackPosition = playerNode.GlobalPosition + AnchorFallbackOffset;
        return new Rect2(fallbackPosition, new Vector2(92f, SelectorButtonSize.Y));
    }

    private static void HideOriginalIdLabel(NRemoteLobbyPlayer playerNode)
    {
        Label? idLabel = TryFindIdLabel(playerNode);
        if (idLabel != null)
        {
            idLabel.Visible = false;
        }
    }

    private static void RestoreOriginalIdLabel(NRemoteLobbyPlayer playerNode)
    {
        Label? idLabel = TryFindIdLabel(playerNode);
        if (idLabel != null)
        {
            idLabel.Visible = true;
        }
    }

    private static Label? TryFindIdLabel(NRemoteLobbyPlayer playerNode)
    {
        string playerIdText = playerNode.PlayerId.ToString();
        foreach (Node child in EnumerateDescendants(playerNode))
        {
            if (child is not Label label)
            {
                continue;
            }

            if (label.Name == WakuuHintName)
            {
                continue;
            }

            string name = label.Name.ToString();
            string text = label.Text ?? string.Empty;
            bool nameLooksLikeId = name.Contains("id", StringComparison.OrdinalIgnoreCase);
            bool textContainsPlayerId = text.Contains(playerIdText, StringComparison.Ordinal);
            if (nameLooksLikeId || textContainsPlayerId)
            {
                return label;
            }
        }

        return null;
    }

    private static IEnumerable<Node> EnumerateDescendants(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            yield return child;
            foreach (Node nested in EnumerateDescendants(child))
            {
                yield return nested;
            }
        }
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
