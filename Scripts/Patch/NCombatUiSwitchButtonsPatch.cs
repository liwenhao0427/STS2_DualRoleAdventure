using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
internal static class NCombatUiReadyPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        LocalCombatSwitchButtons.Ensure(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Process))]
internal static class NCombatUiProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        LocalCombatSwitchButtons.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._ExitTree))]
internal static class NCombatUiExitPatch
{
    [HarmonyPrefix]
    private static void Prefix(NCombatUi __instance)
    {
        LocalCombatSwitchButtons.Remove(__instance);
    }
}

internal static class LocalCombatSwitchButtons
{
    private const string ContainerName = "LocalCombatSwitchContainer";
    private const string UpButtonName = "LocalCombatSwitchUpButton";
    private const string DownButtonName = "LocalCombatSwitchDownButton";

    public static void Ensure(NCombatUi combatUi)
    {
        if (combatUi.GetNodeOrNull<Control>(ContainerName) != null)
        {
            return;
        }

        Control container = new Control
        {
            Name = ContainerName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TopLevel = true,
            ZIndex = 120
        };

        LocalSimpleTextButton upButton = new LocalSimpleTextButton
        {
            Name = UpButtonName,
            ButtonText = "上",
            FocusMode = Control.FocusModeEnum.None,
            Size = new Vector2(72f, 34f),
            CustomMinimumSize = new Vector2(72f, 34f)
        };
        upButton.Connect(
            MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl.SignalName.Released,
            Callable.From<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>((_) =>
                LocalControlSwitchGuard.TrySwitchPrevious("combat-ui-up")));
        container.AddChild(upButton);

        LocalSimpleTextButton downButton = new LocalSimpleTextButton
        {
            Name = DownButtonName,
            ButtonText = "下",
            FocusMode = Control.FocusModeEnum.None,
            Size = new Vector2(72f, 34f),
            CustomMinimumSize = new Vector2(72f, 34f),
            Position = new Vector2(0f, 42f)
        };
        downButton.Connect(
            MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl.SignalName.Released,
            Callable.From<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>((_) =>
                LocalControlSwitchGuard.TrySwitchNext("combat-ui-down")));
        container.AddChild(downButton);

        combatUi.AddChildSafely(container);
        LocalMultiControlLogger.Info("战斗界面已创建 Ping 右侧上下切人按钮。");
    }

    public static void Refresh(NCombatUi combatUi)
    {
        Control? container = combatUi.GetNodeOrNull<Control>(ContainerName);
        if (container == null)
        {
            return;
        }

        NPingButton? pingButton = AccessTools.Field(typeof(NCombatUi), "<PingButton>k__BackingField")?.GetValue(combatUi) as NPingButton;
        bool hasMultiplePlayers = LocalMultiControlRuntime.SessionState.OrderedPlayerIds.Count > 1;
        bool shouldShow = LocalSelfCoopContext.IsEnabled
                          && RunManager.Instance.IsInProgress
                          && CombatManager.Instance.IsInProgress
                          && hasMultiplePlayers
                          && pingButton != null
                          && pingButton.Visible;

        container.Visible = shouldShow;
        if (!shouldShow || pingButton == null)
        {
            return;
        }

        container.GlobalPosition = pingButton.GlobalPosition + new Vector2(pingButton.Size.X + 12f, -2f);
    }

    public static void Remove(NCombatUi combatUi)
    {
        Control? container = combatUi.GetNodeOrNull<Control>(ContainerName);
        container?.QueueFreeSafely();
    }
}
