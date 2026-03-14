using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NMultiplayerPlayerState), nameof(NMultiplayerPlayerState._Ready))]
internal static class NMultiplayerPlayerStateReadyPatch
{
    [HarmonyPostfix]
    private static void Postfix(NMultiplayerPlayerState __instance)
    {
        // 注意：不要再改成直接 Patch NMultiplayerPlayerState._Process。
        // 该目标方法在线上版本曾解析失败并触发 Harmony 初始化异常，导致整包补丁加载不完整。
        // 统一通过 Tracker 节点逐帧刷新，避免再次出现“无法开始游戏”的回归。
        LocalMultiplayerPlayerStateSwitchUi.Ensure(__instance);
    }
}

internal static class LocalMultiplayerPlayerStateSwitchUi
{
    private const string SwitchButtonName = "LocalSwitchPlayerButton";
    private const string RightClickMetaKey = "LocalSwitchPlayerRightClickBound";
    private const string TrackerName = "LocalSwitchPlayerTracker";

    public static void Ensure(NMultiplayerPlayerState state)
    {
        BindRightClick(state);
        EnsureSwitchButton(state);
        EnsureTracker(state);
        Refresh(state);
    }

    public static void Refresh(NMultiplayerPlayerState state)
    {
        LocalSimpleTextButton? button = state.GetNodeOrNull<LocalSimpleTextButton>(SwitchButtonName);
        if (button == null)
        {
            return;
        }

        bool shouldShow = LocalSelfCoopContext.IsEnabled && RunManager.Instance.IsInProgress;
        button.Visible = shouldShow;
        if (!shouldShow)
        {
            return;
        }

        button.ButtonText = $"切{LocalSelfCoopContext.GetSlotLabel(state.Player.NetId)}";

        // 注意：固定 X 轴对齐是用户明确要求，不能再按 Hitbox 宽度动态计算。
        // 之前 state.Hitbox.Size.X 会因名称/状态变化而偏移，造成三行按钮参差不齐。
        float fixedX = state.GetViewport().GetVisibleRect().Size.X * (430f / NGame.devResolution.X);
        button.GlobalPosition = new Vector2(fixedX, state.GlobalPosition.Y + 8f);
    }

    private static void EnsureSwitchButton(NMultiplayerPlayerState state)
    {
        if (state.GetNodeOrNull<LocalSimpleTextButton>(SwitchButtonName) != null)
        {
            return;
        }

        LocalSimpleTextButton button = new LocalSimpleTextButton
        {
            Name = SwitchButtonName,
            ButtonText = "切",
            FocusMode = Control.FocusModeEnum.None,
            FontSize = 18,
            Size = new Vector2(68f, 32f),
            CustomMinimumSize = new Vector2(68f, 32f),
            TopLevel = true,
            ZIndex = 100
        };
        button.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => TrySwitchToPlayer(state.Player, "player-state-button")));
        state.AddChildSafely(button);
    }

    private static void EnsureTracker(NMultiplayerPlayerState state)
    {
        if (state.GetNodeOrNull<LocalPlayerStateSwitchTracker>(TrackerName) != null)
        {
            return;
        }

        LocalPlayerStateSwitchTracker tracker = new LocalPlayerStateSwitchTracker
        {
            Name = TrackerName
        };
        tracker.Initialize(state);
        state.AddChild(tracker);
    }

    private static void BindRightClick(NMultiplayerPlayerState state)
    {
        if (state.Hitbox.HasMeta(RightClickMetaKey))
        {
            return;
        }

        state.Hitbox.SetMeta(RightClickMetaKey, true);
        state.Hitbox.Connect(
            NClickableControl.SignalName.MouseReleased,
            Callable.From<InputEvent>((inputEvent) => OnHitboxMouseReleased(state, inputEvent)));
    }

    private static void OnHitboxMouseReleased(NMultiplayerPlayerState state, InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton mouseButton ||
            mouseButton.ButtonIndex != MouseButton.Right ||
            mouseButton.IsPressed())
        {
            return;
        }

        TrySwitchToPlayer(state.Player, "player-state-right-click");
    }

    private static void TrySwitchToPlayer(Player player, string source)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        LocalControlSwitchGuard.TrySwitchTo(player.NetId, source);
    }
}

internal sealed partial class LocalPlayerStateSwitchTracker : Node
{
    private NMultiplayerPlayerState? _state;

    public void Initialize(NMultiplayerPlayerState state)
    {
        _state = state;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_state == null || !GodotObject.IsInstanceValid(_state))
        {
            QueueFree();
            return;
        }

        LocalMultiplayerPlayerStateSwitchUi.Refresh(_state);
    }
}
