using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
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

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
internal static class NCombatUiActivatePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance, CombatState state)
    {
        LocalCombatSwitchButtons.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Enable))]
internal static class NCombatUiEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        LocalCombatSwitchButtons.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Disable))]
internal static class NCombatUiDisablePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        LocalCombatSwitchButtons.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Deactivate))]
internal static class NCombatUiDeactivatePatch
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
    private const string TrackerName = "LocalCombatSwitchTracker";
    private static readonly Vector2 PingShowPosRatio = new Vector2(1536f, 932f) / NGame.devResolution;
    private static readonly Vector2 ButtonOffset = new Vector2(148f, 4f);

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
            FontSize = 15,
            Size = new Vector2(34f, 24f),
            CustomMinimumSize = new Vector2(34f, 24f)
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
            FontSize = 15,
            Size = new Vector2(34f, 24f),
            CustomMinimumSize = new Vector2(34f, 24f),
            Position = new Vector2(0f, 26f)
        };
        downButton.Connect(
            MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl.SignalName.Released,
            Callable.From<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>((_) =>
                LocalControlSwitchGuard.TrySwitchNext("combat-ui-down")));
        container.AddChild(downButton);

        combatUi.AddChildSafely(container);
        EnsureTracker(combatUi);
        LocalMultiControlLogger.Info("战斗界面已创建 Ping 右侧上下切人按钮。");
    }

    private static void EnsureTracker(NCombatUi combatUi)
    {
        if (combatUi.GetNodeOrNull<LocalCombatSwitchTracker>(TrackerName) != null)
        {
            return;
        }

        // 注意：不要再用 NCombatUi._Process 打补丁。
        // 该方法在目标版本不存在，曾导致 Harmony 初始化失败，进而引发“无法开始/读档异常”。
        // 这里改为挂一个本地跟随节点逐帧刷新，兼容性更稳。
        LocalCombatSwitchTracker tracker = new LocalCombatSwitchTracker
        {
            Name = TrackerName
        };
        tracker.Initialize(combatUi);
        combatUi.AddChild(tracker);
    }

    public static void Refresh(NCombatUi combatUi)
    {
        Control? container = combatUi.GetNodeOrNull<Control>(ContainerName);
        if (container == null)
        {
            return;
        }

        int runPlayerCount = RunManager.Instance.DebugOnlyGetState()?.Players.Count ?? 0;
        bool hasMultiplePlayers = LocalMultiControlRuntime.SessionState.OrderedPlayerIds.Count > 1 || runPlayerCount > 1;
        bool shouldShow = LocalSelfCoopContext.IsEnabled
                          && RunManager.Instance.IsInProgress
                          && CombatManager.Instance.IsInProgress
                          && hasMultiplePlayers;

        container.Visible = shouldShow;
        if (!shouldShow)
        {
            return;
        }

        Viewport? viewport = combatUi.GetViewport();
        if (viewport == null)
        {
            return;
        }

        Vector2 anchor = PingShowPosRatio * viewport.GetVisibleRect().Size;
        container.GlobalPosition = anchor + ButtonOffset;
    }

    public static void Remove(NCombatUi combatUi)
    {
        Control? container = combatUi.GetNodeOrNull<Control>(ContainerName);
        container?.QueueFreeSafely();
    }
}

internal sealed partial class LocalCombatSwitchTracker : Node
{
    private NCombatUi? _combatUi;

    public void Initialize(NCombatUi combatUi)
    {
        _combatUi = combatUi;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_combatUi == null || !GodotObject.IsInstanceValid(_combatUi))
        {
            QueueFree();
            return;
        }

        LocalCombatSwitchButtons.Refresh(_combatUi);
    }
}
