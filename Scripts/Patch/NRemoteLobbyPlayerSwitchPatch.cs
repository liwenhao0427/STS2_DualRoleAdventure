using System;
using System.Collections.Generic;
using System.Linq;
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
    private const string GlobalWakuuToggleName = "LocalLobbyGlobalWakuuToggle";
    private const string GlobalWakuuLabelName = "LocalLobbyGlobalWakuuLabel";

    private const float ColumnMergeTolerance = 40f;
    private const float MinColumnGap = 140f;
    private const float ColumnRightShift = 18f;

    private static readonly Vector2 SelectorButtonSize = new(52f, 28f);
    private static readonly Vector2 WakuuToggleSize = new(30f, 28f);
    private static readonly Vector2 GlobalToggleSize = new(44f, 28f);
    private static readonly Vector2 AnchorFallbackOffset = new(128f, 3f);
    private static readonly Vector2 GlobalToggleTopLeft = new(24f, 24f);

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

        NCharacterSelectScreen? screen = TryGetCharacterSelectScreen(playerNode);
        bool shouldShow = LocalSelfCoopContext.IsEnabled
                          && !RunManager.Instance.IsInProgress
                          && screen != null
                          && LocalSelfCoopContext.LocalPlayerIds.Contains(playerNode.PlayerId);

        button.Visible = shouldShow;
        wakuuToggle.Visible = shouldShow;
        wakuuHint.Visible = false;

        if (!shouldShow || screen == null)
        {
            RestoreOriginalIdLabel(playerNode);
            RefreshGlobalWakuuToggle(screen);
            return;
        }

        string slotLabel = LocalSelfCoopContext.GetSlotLabel(playerNode.PlayerId);
        button.ButtonText = LocalModText.RoleSlot(slotLabel);
        AnchorLayout layout = ResolveAnchorLayout(screen, playerNode);
        HideOriginalIdLabel(playerNode);

        button.GlobalPosition = new Vector2(layout.ColumnX, layout.AnchorRect.Position.Y - 1f);
        button.Size = SelectorButtonSize;
        button.CustomMinimumSize = SelectorButtonSize;

        bool wakuuEnabled = LocalSelfCoopContext.IsWakuuEnabled(playerNode.PlayerId);
        wakuuToggle.SetPressedNoSignal(wakuuEnabled);
        wakuuToggle.GlobalPosition = button.GlobalPosition + new Vector2(button.Size.X + 4f, 0f);
        wakuuToggle.Size = WakuuToggleSize;
        wakuuToggle.CustomMinimumSize = WakuuToggleSize;

        RefreshGlobalWakuuToggle(screen);
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

        playerNode.AddChildSafely(hint);
    }

    private static void EnsureGlobalWakuuToggle(NCharacterSelectScreen screen)
    {
        if (screen.GetNodeOrNull<CheckButton>(GlobalWakuuToggleName) == null)
        {
            CheckButton toggle = new()
            {
                Name = GlobalWakuuToggleName,
                Text = string.Empty,
                FocusMode = Control.FocusModeEnum.None,
                MouseFilter = Control.MouseFilterEnum.Stop,
                Size = GlobalToggleSize,
                CustomMinimumSize = GlobalToggleSize,
                TopLevel = true,
                ZIndex = 92
            };

            toggle.Connect(
                BaseButton.SignalName.Toggled,
                Callable.From<bool>((pressed) =>
                    LocalSelfCoopContext.SetAllWakuuEnabled(pressed, "char-select-global-wakuu-toggle")));
            screen.AddChildSafely(toggle);
        }

        if (screen.GetNodeOrNull<Label>(GlobalWakuuLabelName) == null)
        {
            Label label = new()
            {
                Name = GlobalWakuuLabelName,
                Text = LocalModText.GlobalWakuuLabel,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                TopLevel = true,
                ZIndex = 92
            };
            label.AddThemeFontSizeOverride("font_size", 16);
            label.AddThemeColorOverride("font_color", new Color("f3efe6"));
            label.AddThemeColorOverride("font_outline_color", new Color("111111"));
            label.AddThemeConstantOverride("outline_size", 4);
            screen.AddChildSafely(label);
        }
    }

    private static void RefreshGlobalWakuuToggle(NCharacterSelectScreen? screen)
    {
        if (screen == null || !GodotObject.IsInstanceValid(screen))
        {
            return;
        }

        EnsureGlobalWakuuToggle(screen);

        CheckButton? toggle = screen.GetNodeOrNull<CheckButton>(GlobalWakuuToggleName);
        Label? label = screen.GetNodeOrNull<Label>(GlobalWakuuLabelName);
        if (toggle == null || label == null)
        {
            return;
        }

        List<NRemoteLobbyPlayer> localNodes = GetLocalLobbyNodes(screen);
        bool shouldShow = LocalSelfCoopContext.IsEnabled && !RunManager.Instance.IsInProgress && localNodes.Count > 0;
        toggle.Visible = shouldShow;
        label.Visible = shouldShow;
        if (!shouldShow)
        {
            return;
        }

        label.Text = LocalModText.GlobalWakuuLabel;
        toggle.GlobalPosition = GlobalToggleTopLeft;
        label.GlobalPosition = toggle.GlobalPosition + new Vector2(toggle.Size.X + 8f, 4f);

        List<ulong> activeIds = LocalSelfCoopContext.LocalPlayerIds
            .Take(LocalSelfCoopContext.DesiredLocalPlayerCount)
            .ToList();
        bool allEnabled = activeIds.Count > 0 && activeIds.All(LocalSelfCoopContext.IsWakuuEnabled);
        toggle.SetPressedNoSignal(allEnabled);
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

    private static NCharacterSelectScreen? TryGetCharacterSelectScreen(Node node)
    {
        for (Node? current = node; current != null; current = current.GetParent())
        {
            if (current is NCharacterSelectScreen screen)
            {
                return screen;
            }
        }

        return null;
    }

    private static AnchorLayout ResolveAnchorLayout(NCharacterSelectScreen screen, NRemoteLobbyPlayer playerNode)
    {
        List<NRemoteLobbyPlayer> localNodes = GetLocalLobbyNodes(screen);
        Rect2 currentAnchorRect = ResolveIdAnchorRect(playerNode);
        if (localNodes.Count == 0)
        {
            return new AnchorLayout(currentAnchorRect, currentAnchorRect.Position.X + ColumnRightShift);
        }

        List<float> columns = BuildColumns(localNodes.Select((node) => node.GlobalPosition.X).ToList());
        int columnIndex = ResolveColumnIndex(columns, playerNode.GlobalPosition.X);
        float step = ResolveColumnStep(columns);

        float minX = columns.Min();
        float firstColumnX = localNodes
            .Where((node) => Mathf.Abs(node.GlobalPosition.X - minX) <= ColumnMergeTolerance)
            .Select((node) => ResolveIdAnchorRect(node).Position.X)
            .DefaultIfEmpty(currentAnchorRect.Position.X)
            .Min();

        float columnX = firstColumnX + ColumnRightShift + columnIndex * step;
        return new AnchorLayout(currentAnchorRect, columnX);
    }

    private static List<float> BuildColumns(List<float> values)
    {
        values.Sort();
        List<float> columns = new();
        foreach (float value in values)
        {
            if (columns.Count == 0)
            {
                columns.Add(value);
                continue;
            }

            if (Mathf.Abs(columns[^1] - value) <= ColumnMergeTolerance)
            {
                columns[^1] = (columns[^1] + value) * 0.5f;
            }
            else
            {
                columns.Add(value);
            }
        }

        return columns;
    }

    private static int ResolveColumnIndex(List<float> columns, float x)
    {
        if (columns.Count == 0)
        {
            return 0;
        }

        int bestIndex = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < columns.Count; i++)
        {
            float distance = Mathf.Abs(columns[i] - x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static float ResolveColumnStep(List<float> columns)
    {
        if (columns.Count <= 1)
        {
            return MinColumnGap;
        }

        List<float> gaps = new();
        for (int i = 1; i < columns.Count; i++)
        {
            float gap = columns[i] - columns[i - 1];
            if (gap > 1f)
            {
                gaps.Add(gap);
            }
        }

        if (gaps.Count == 0)
        {
            return MinColumnGap;
        }

        return Mathf.Max(MinColumnGap, gaps.Average());
    }

    private static List<NRemoteLobbyPlayer> GetLocalLobbyNodes(NCharacterSelectScreen screen)
    {
        return EnumerateDescendants(screen)
            .OfType<NRemoteLobbyPlayer>()
            .Where((node) => LocalSelfCoopContext.LocalPlayerIds.Contains(node.PlayerId))
            .OrderBy((node) => node.GlobalPosition.X)
            .ThenBy((node) => node.GlobalPosition.Y)
            .ToList();
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

            if (label.Name == WakuuHintName || label.Name == GlobalWakuuLabelName)
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

    private readonly record struct AnchorLayout(Rect2 AnchorRect, float ColumnX);
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
