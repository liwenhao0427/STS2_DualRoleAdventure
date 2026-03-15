using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
internal static class NCharacterSelectWakuuToggleOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        LocalCharacterSelectWakuuToggles.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Process))]
internal static class NCharacterSelectWakuuToggleProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        LocalCharacterSelectWakuuToggles.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuClosed))]
internal static class NCharacterSelectWakuuToggleClosePatch
{
    [HarmonyPrefix]
    private static void Prefix(NCharacterSelectScreen __instance)
    {
        LocalCharacterSelectWakuuToggles.Remove(__instance);
    }
}

internal static class LocalCharacterSelectWakuuToggles
{
    private const string PanelName = "LocalSelfCoopWakuuPanel";
    private const string ButtonPrefix = "LocalSelfCoopWakuuButton_";
    private const string WakuuTextUnchecked = "-[ ] \u74e6\u5e93";
    private const string WakuuTextChecked = "-[x] \u74e6\u5e93";
    private static readonly Vector2 ToggleSize = new Vector2(136f, 30f);
    private const float RightOffset = 12f;
    private const float CollisionOffsetY = 34f;

    public static void Sync(NCharacterSelectScreen screen)
    {
        if (!LocalSelfCoopContext.IsEnabled || RunManager.Instance.IsInProgress || !GodotObject.IsInstanceValid(screen))
        {
            Remove(screen);
            return;
        }

        StartRunLobby? lobby = AccessTools.Field(typeof(NCharacterSelectScreen), "_lobby")?.GetValue(screen) as StartRunLobby;
        Control? charButtonContainer = AccessTools.Field(typeof(NCharacterSelectScreen), "_charButtonContainer")
            ?.GetValue(screen) as Control;
        if (lobby == null || charButtonContainer == null)
        {
            return;
        }

        Control panel = EnsurePanel(screen);
        HashSet<ulong> activePlayerIds = new HashSet<ulong>();
        Dictionary<NCharacterSelectButton, int> overlapIndexByButton = new Dictionary<NCharacterSelectButton, int>();

        foreach (LobbyPlayer player in lobby.Players.Where((candidate) => LocalSelfCoopContext.LocalPlayerIds.Contains(candidate.id)))
        {
            NCharacterSelectButton? button = FindCharacterButton(charButtonContainer, player.character);
            if (button == null)
            {
                continue;
            }

            activePlayerIds.Add(player.id);
            Button toggle = EnsureToggleButton(panel, player.id);
            toggle.Text = LocalSelfCoopContext.IsWakuuEnabled(player.id) ? WakuuTextChecked : WakuuTextUnchecked;
            toggle.Visible = true;

            int overlapIndex = overlapIndexByButton.TryGetValue(button, out int current) ? current : 0;
            overlapIndexByButton[button] = overlapIndex + 1;

            Vector2 basePosition = button.GlobalPosition + new Vector2(button.Size.X + RightOffset, (button.Size.Y - ToggleSize.Y) * 0.5f);
            toggle.GlobalPosition = basePosition + new Vector2(0f, overlapIndex * CollisionOffsetY);
        }

        foreach (Button toggle in panel.GetChildren().OfType<Button>())
        {
            ulong playerId = ParsePlayerId(toggle.Name.ToString());
            if (!activePlayerIds.Contains(playerId))
            {
                toggle.Visible = false;
            }
        }
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
            TopLevel = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 95
        };

        screen.AddChildSafely(panel);
        return panel;
    }

    private static Button EnsureToggleButton(Control panel, ulong playerId)
    {
        string nodeName = $"{ButtonPrefix}{playerId}";
        Button? existing = panel.GetNodeOrNull<Button>(nodeName);
        if (existing != null)
        {
            return existing;
        }

        Button toggle = new Button
        {
            Name = nodeName,
            Size = ToggleSize,
            CustomMinimumSize = ToggleSize,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        toggle.Connect(BaseButton.SignalName.Pressed, Callable.From(() => OnTogglePressed(playerId)));
        panel.AddChild(toggle);
        return toggle;
    }

    private static NCharacterSelectButton? FindCharacterButton(Control charButtonContainer, CharacterModel character)
    {
        return charButtonContainer.GetChildren()
            .OfType<NCharacterSelectButton>()
            .FirstOrDefault((button) => button.Character == character);
    }

    private static ulong ParsePlayerId(string nodeName)
    {
        if (!nodeName.StartsWith(ButtonPrefix))
        {
            return 0;
        }

        string idPart = nodeName.Substring(ButtonPrefix.Length);
        return ulong.TryParse(idPart, out ulong parsed) ? parsed : 0;
    }

    private static void OnTogglePressed(ulong playerId)
    {
        bool enabled = !LocalSelfCoopContext.IsWakuuEnabled(playerId);
        LocalSelfCoopContext.SetWakuuEnabled(playerId, enabled, "character-select-wakuu-toggle");
    }
}
