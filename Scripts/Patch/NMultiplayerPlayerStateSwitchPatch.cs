using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
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
        LocalMultiplayerPlayerStateSwitchUi.Ensure(__instance);
    }
}

[HarmonyPatch(typeof(NMultiplayerPlayerState), nameof(NMultiplayerPlayerState._Process))]
internal static class NMultiplayerPlayerStateProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NMultiplayerPlayerState __instance)
    {
        LocalMultiplayerPlayerStateSwitchUi.Refresh(__instance);
    }
}

internal static class LocalMultiplayerPlayerStateSwitchUi
{
    private const string SwitchButtonName = "LocalSwitchPlayerButton";
    private const string RightClickMetaKey = "LocalSwitchPlayerRightClickBound";

    public static void Ensure(NMultiplayerPlayerState state)
    {
        BindRightClick(state);
        EnsureSwitchButton(state);
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
        button.GlobalPosition = state.GlobalPosition + new Vector2(state.Hitbox.Size.X + 10f, 10f);
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
            ButtonText = "切换",
            FocusMode = Control.FocusModeEnum.None,
            Size = new Vector2(72f, 34f),
            CustomMinimumSize = new Vector2(72f, 34f),
            TopLevel = true,
            ZIndex = 100
        };
        button.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => TrySwitchToPlayer(state.Player, "player-state-button")));
        state.AddChildSafely(button);
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
