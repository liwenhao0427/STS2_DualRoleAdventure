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
    private const string TrackerName = "LocalLobbyPlayerSwitchTracker";

    public static void Ensure(NRemoteLobbyPlayer playerNode)
    {
        EnsureButton(playerNode);
        EnsureTracker(playerNode);
        Refresh(playerNode);
    }

    public static void Refresh(NRemoteLobbyPlayer playerNode)
    {
        LocalSimpleTextButton? button = playerNode.GetNodeOrNull<LocalSimpleTextButton>(ButtonName);
        if (button == null)
        {
            return;
        }

        bool inCharacterSelect = IsInsideCharacterSelect(playerNode);
        bool shouldShow = LocalSelfCoopContext.IsEnabled
                          && !RunManager.Instance.IsInProgress
                          && inCharacterSelect
                          && LocalSelfCoopContext.LocalPlayerIds.Contains(playerNode.PlayerId);
        button.Visible = shouldShow;
        if (!shouldShow)
        {
            return;
        }

        button.ButtonText = string.Empty;

        // 右移固定轴，避免覆盖左侧玩家信息。
        float fixedX = playerNode.GlobalPosition.X + playerNode.Size.X + 72f;
        button.GlobalPosition = new Vector2(fixedX, playerNode.GlobalPosition.Y + 2f);
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
