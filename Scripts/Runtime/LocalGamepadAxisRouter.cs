using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalGamepadAxisRouter
{
    private const string PollerName = "LocalGamepadAxisPoller";

    private const float TriggerThreshold = 0.55f;
    private const float ReleaseThreshold = 0.30f;

    private const ulong RepeatStartDelayMs = 220;
    private const ulong RepeatIntervalMs = 90;

    private static int _horizontalDirection;
    private static int _verticalDirection;

    private static ulong _nextHorizontalRepeatAtMs;
    private static ulong _nextVerticalRepeatAtMs;

    public static void EnsurePollerAttached()
    {
        NGame? game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game))
        {
            return;
        }

        if (game.GetNodeOrNull<LocalGamepadAxisPoller>(PollerName) != null)
        {
            return;
        }

        LocalGamepadAxisPoller poller = new()
        {
            Name = PollerName
        };
        game.AddChild(poller);
    }

    public static void Tick()
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            Reset();
            return;
        }

        int? joypadId = TryGetJoypadId();
        if (!joypadId.HasValue)
        {
            Reset();
            return;
        }

        float axisX = Input.GetJoyAxis(joypadId.Value, JoyAxis.LeftX);
        float axisY = Input.GetJoyAxis(joypadId.Value, JoyAxis.LeftY);
        ulong now = Time.GetTicksMsec();

        bool isInRun = RunManager.Instance.IsInProgress;
        bool preferVertical = Mathf.Abs(axisY) >= Mathf.Abs(axisX);

        int verticalTarget = ResolveDirectionWithHysteresis(axisY, _verticalDirection);
        HandleVertical(verticalTarget, now, isInRun);

        int horizontalTarget = 0;
        if (!isInRun && !preferVertical)
        {
            horizontalTarget = ResolveDirectionWithHysteresis(axisX, _horizontalDirection);
        }

        HandleHorizontal(horizontalTarget, now);
    }

    private static void HandleVertical(int targetDirection, ulong now, bool isInRun)
    {
        if (targetDirection == 0)
        {
            _verticalDirection = 0;
            return;
        }

        if (_verticalDirection != targetDirection)
        {
            _verticalDirection = targetDirection;
            TriggerVertical(targetDirection, isInRun);
            _nextVerticalRepeatAtMs = now + RepeatStartDelayMs;
            return;
        }

        if (now < _nextVerticalRepeatAtMs)
        {
            return;
        }

        TriggerVertical(targetDirection, isInRun);
        _nextVerticalRepeatAtMs = now + RepeatIntervalMs;
    }

    private static void HandleHorizontal(int targetDirection, ulong now)
    {
        if (targetDirection == 0)
        {
            _horizontalDirection = 0;
            return;
        }

        if (_horizontalDirection != targetDirection)
        {
            _horizontalDirection = targetDirection;
            TriggerHorizontal(targetDirection);
            _nextHorizontalRepeatAtMs = now + RepeatStartDelayMs;
            return;
        }

        if (now < _nextHorizontalRepeatAtMs)
        {
            return;
        }

        TriggerHorizontal(targetDirection);
        _nextHorizontalRepeatAtMs = now + RepeatIntervalMs;
    }

    private static void TriggerVertical(int direction, bool isInRun)
    {
        if (isInRun)
        {
            if (direction < 0)
            {
                _ = LocalControlSwitchGuard.TrySwitchPrevious("gamepad:left-stick-up");
                return;
            }

            _ = LocalControlSwitchGuard.TrySwitchNext("gamepad:left-stick-down");
            return;
        }

        _ = LocalSelfCoopContext.SwitchLobbyEditingPlayer(next: direction > 0);
    }

    private static void TriggerHorizontal(int direction)
    {
        _ = LocalSelfCoopContext.AdjustDesiredLocalPlayerCount(direction > 0 ? 1 : -1, "gamepad:left-stick-x");
    }

    private static int ResolveDirectionWithHysteresis(float axisValue, int currentDirection)
    {
        if (currentDirection == 0)
        {
            if (axisValue >= TriggerThreshold)
            {
                return 1;
            }

            if (axisValue <= -TriggerThreshold)
            {
                return -1;
            }

            return 0;
        }

        if (currentDirection > 0)
        {
            if (axisValue <= -TriggerThreshold)
            {
                return -1;
            }

            return axisValue <= ReleaseThreshold ? 0 : 1;
        }

        if (axisValue >= TriggerThreshold)
        {
            return 1;
        }

        return axisValue >= -ReleaseThreshold ? 0 : -1;
    }

    private static int? TryGetJoypadId()
    {
        Godot.Collections.Array<int> connectedJoypads = Input.GetConnectedJoypads();
        if (connectedJoypads.Count == 0)
        {
            return null;
        }

        return connectedJoypads[0];
    }

    private static void Reset()
    {
        _horizontalDirection = 0;
        _verticalDirection = 0;
        _nextHorizontalRepeatAtMs = 0;
        _nextVerticalRepeatAtMs = 0;
    }
}

internal sealed partial class LocalGamepadAxisPoller : Node
{
    public override void _Ready()
    {
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        LocalGamepadAxisRouter.Tick();
    }
}
