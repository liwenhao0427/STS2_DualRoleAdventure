using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Generate))]
internal static class RestSiteOptionPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref List<RestSiteOption> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        __result.RemoveAll((option) => option is MendRestSiteOption);
    }
}

[HarmonyPatch(typeof(HealRestSiteOption), nameof(HealRestSiteOption.OnSelect))]
internal static class HealRestSiteOptionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(HealRestSiteOption __instance, ref Task<bool> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        __result = HealAllPlayersAsync(__instance);
        return false;
    }

    private static async Task<bool> HealAllPlayersAsync(HealRestSiteOption option)
    {
        Player? owner = AccessTools.Field(typeof(RestSiteOption), "<Owner>k__BackingField")?.GetValue(option) as Player;
        if (owner == null)
        {
            return false;
        }

        if (owner.RunState == null || owner.RunState.Players.Count <= 1)
        {
            await HealRestSiteOption.ExecuteRestSiteHeal(owner, isMimicked: false);
            return true;
        }

        await HealRestSiteOption.ExecuteRestSiteHeal(owner, isMimicked: false);
        foreach (Player player in owner.RunState.Players)
        {
            if (player.NetId == owner.NetId)
            {
                continue;
            }

            await CreatureCmd.Heal(player.Creature, HealRestSiteOption.GetHealAmount(player));
            await Hook.AfterRestSiteHeal(player.RunState, player, isMimicked: true);
        }

        LocalMultiControlLogger.Info("休息区恢复已改为全体恢复。");
        return true;
    }
}

[HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.ChooseLocalOption))]
internal static class RestSiteSynchronizerChooseLocalOptionPatch
{
    private static ulong? _pendingSwitchTargetPlayerId;
    private static int? _pendingOptionIndex;

    [HarmonyPostfix]
    private static void Postfix(RestSiteSynchronizer __instance, int index, ref Task<bool> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        __result = WrapChooseLocalOptionAsync(__instance, index, __result);
    }

    private static async Task<bool> WrapChooseLocalOptionAsync(RestSiteSynchronizer synchronizer, int optionIndex, Task<bool> originalTask)
    {
        ulong? localPlayerId = LocalContext.NetId;
        if (!localPlayerId.HasValue)
        {
            localPlayerId = ReadLocalPlayerIdFromSynchronizer(synchronizer);
        }

        List<RestSiteOption> sourceOptionsSnapshot = new List<RestSiteOption>();
        if (localPlayerId.HasValue)
        {
            sourceOptionsSnapshot = synchronizer.GetOptionsForPlayer(localPlayerId.Value).ToList();
        }

        bool success = await originalTask;
        if (!localPlayerId.HasValue)
        {
            LocalMultiControlLogger.Warn($"休息区升级切换失败：无法识别当前本地角色，optionIndex={optionIndex}");
            return success;
        }

        if (!success)
        {
            LocalMultiControlLogger.Warn(
                $"休息区选项执行失败，不触发自动切人: player={localPlayerId.Value}, optionIndex={optionIndex}, snapshot={DescribeOptions(sourceOptionsSnapshot)}");
            return success;
        }

        ulong? nextPlayerId = FindNextPlayerWithAnyOption(synchronizer, localPlayerId.Value);
        if (!nextPlayerId.HasValue)
        {
            LocalMultiControlLogger.Info($"休息区已无待选角色，流程结束: source={localPlayerId.Value}");
            return success;
        }

        _pendingSwitchTargetPlayerId = nextPlayerId.Value;
        _pendingOptionIndex = optionIndex;
        LocalMultiControlLogger.Info(
            $"休息区选择成功，已排队切换到下一位待选角色并续传同索引选项: {localPlayerId.Value} -> {nextPlayerId.Value}, optionIndex={optionIndex}, snapshot={DescribeOptions(sourceOptionsSnapshot)}");

        return success;
    }

    internal static bool TryConsumePendingSwitchTarget(out ulong playerId, out int optionIndex)
    {
        if (_pendingSwitchTargetPlayerId.HasValue)
        {
            playerId = _pendingSwitchTargetPlayerId.Value;
            optionIndex = _pendingOptionIndex ?? -1;
            _pendingSwitchTargetPlayerId = null;
            _pendingOptionIndex = null;
            return true;
        }

        playerId = 0;
        optionIndex = -1;
        return false;
    }

    private static ulong? ReadLocalPlayerIdFromSynchronizer(RestSiteSynchronizer synchronizer)
    {
        object? fieldValue = AccessTools.Field(typeof(RestSiteSynchronizer), "_localPlayerId")?.GetValue(synchronizer);
        if (fieldValue is ulong fieldPlayerId)
        {
            return fieldPlayerId;
        }

        return null;
    }

    private static ulong? FindNextPlayerWithAnyOption(RestSiteSynchronizer synchronizer, ulong sourcePlayerId)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null || runState.Players.Count <= 1)
        {
            return null;
        }

        List<Player> players = runState.Players.ToList();
        int sourceIndex = players.FindIndex((player) => player.NetId == sourcePlayerId);
        if (sourceIndex < 0)
        {
            return null;
        }

        for (int step = 1; step < players.Count; step++)
        {
            int index = (sourceIndex + step) % players.Count;
            Player candidate = players[index];
            IReadOnlyList<RestSiteOption> options = synchronizer.GetOptionsForPlayer(candidate.NetId);
            if (options.Count > 0)
            {
                return candidate.NetId;
            }
        }

        return null;
    }

    private static string DescribeOptions(IReadOnlyList<RestSiteOption> options)
    {
        if (options.Count == 0)
        {
            return "[]";
        }

        return "[" + string.Join(", ", options.Select((option, index) => $"{index}:{option.GetType().Name}")) + "]";
    }
}

[HarmonyPatch(typeof(NRestSiteRoom), "AfterSelectingOptionAsync")]
internal static class NRestSiteRoomAfterSelectingOptionPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (!RestSiteSynchronizerChooseLocalOptionPatch.TryConsumePendingSwitchTarget(out ulong targetPlayerId, out int optionIndex))
        {
            return;
        }

        __result = WaitRoomSelectionAndSwitchAsync(__result, targetPlayerId, optionIndex);
    }

    private static async Task WaitRoomSelectionAndSwitchAsync(Task originalTask, ulong targetPlayerId, int optionIndex)
    {
        await originalTask;
        await Task.Yield();
        Callable.From(delegate
        {
            LocalMultiControlRuntime.SwitchControlledPlayerTo(targetPlayerId, "rest-site-next-mandatory-choice");

            if (optionIndex < 0)
            {
                return;
            }

            IReadOnlyList<RestSiteOption> targetOptions = RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(targetPlayerId);
            if (optionIndex >= targetOptions.Count)
            {
                LocalMultiControlLogger.Info(
                    $"休息区续传索引已跳过：目标角色选项数量不足，target={targetPlayerId}, optionIndex={optionIndex}, options={targetOptions.Count}");
                return;
            }

            TaskHelper.RunSafely(RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(optionIndex));
            LocalMultiControlLogger.Info(
                $"休息区已为下一位角色续传同索引选项: target={targetPlayerId}, optionIndex={optionIndex}");
        }).CallDeferred();
    }
}

[HarmonyPatch(typeof(NRestSiteRoom), "OnPlayerChangedHoveredRestSiteOption")]
internal static class NRestSiteRoomHoverGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NRestSiteRoom __instance, ulong playerId)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        IRunState? runState = AccessTools.Field(typeof(NRestSiteRoom), "_runState")?.GetValue(__instance) as IRunState;
        if (runState == null || runState.Players.Count <= 1)
        {
            return false;
        }

        NRestSiteCharacter? character = __instance.Characters.FirstOrDefault((candidate) => candidate.Player.NetId == playerId);
        if (character == null)
        {
            return false;
        }

        int? hoveredOptionIndex = RunManager.Instance.RestSiteSynchronizer.GetHoveredOptionIndex(playerId);
        RestSiteOption? option = null;
        if (hoveredOptionIndex.HasValue)
        {
            IReadOnlyList<RestSiteOption> options = RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(playerId);
            if (hoveredOptionIndex.Value >= 0 && hoveredOptionIndex.Value < options.Count)
            {
                option = options[hoveredOptionIndex.Value];
            }
            else
            {
                LocalMultiControlLogger.Warn($"休息区悬停索引越界，已忽略: player={playerId}, index={hoveredOptionIndex.Value}, options={options.Count}");
            }
        }

        character.ShowHoveredRestSiteOption(option);
        return false;
    }
}

[HarmonyPatch(typeof(NRestSiteButton), "SelectOption")]
internal static class NRestSiteButtonSelectGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(RestSiteOption option, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        NRestSiteRoom? room = NRestSiteRoom.Instance;
        if (room == null)
        {
            return true;
        }

        if (FindOptionIndex(room.Options, option) >= 0)
        {
            return true;
        }

        RestSiteUiRefreshUtil.TryRefresh("button-option-mismatch");
        room = NRestSiteRoom.Instance;
        if (room != null && FindOptionIndex(room.Options, option) >= 0)
        {
            return true;
        }

        RunManager.Instance.RestSiteSynchronizer.LocalOptionHovered(null);
        LocalMultiControlLogger.Warn("休息区按钮与当前选项列表不一致，已拒绝本次点击并刷新。");
        __result = Task.CompletedTask;
        return false;
    }

    private static int FindOptionIndex(IReadOnlyList<RestSiteOption> options, RestSiteOption target)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] == target)
            {
                return i;
            }
        }

        return -1;
    }
}

internal static class RestSiteUiRefreshUtil
{
    internal static bool TryRefresh(string source)
    {
        NRestSiteRoom? room = NRestSiteRoom.Instance;
        if (room == null)
        {
            return false;
        }

        try
        {
            RunManager.Instance.RestSiteSynchronizer.LocalOptionHovered(null);
            AccessTools.Field(typeof(NRestSiteRoom), "_lastFocused")?.SetValue(room, null);
            AccessTools.Method(typeof(NRestSiteRoom), "UpdateRestSiteOptions")?.Invoke(room, null);
            LocalMultiControlLogger.Info($"休息区选项已刷新: source={source}, player={LocalContext.NetId?.ToString() ?? "null"}");
            return true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"休息区选项刷新失败: source={source}, error={exception.Message}");
            return false;
        }
    }
}
