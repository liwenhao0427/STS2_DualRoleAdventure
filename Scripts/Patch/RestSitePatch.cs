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
        // 需求调整：休息区保留原多人联机选项，不再删减。
    }
}

[HarmonyPatch(typeof(HealRestSiteOption), nameof(HealRestSiteOption.OnSelect))]
internal static class HealRestSiteOptionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(HealRestSiteOption __instance, ref Task<bool> __result)
    {
        // 需求调整：休息区回血按角色独立结算，不再拦截为全体恢复。
        return true;
    }
}

[HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.ChooseLocalOption))]
internal static class RestSiteSynchronizerChooseLocalOptionPatch
{
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

        // 固定策略：每位角色都必须各选一次，当前角色成功后切到下一位仍有选项的角色。
        Callable.From(delegate
        {
            LocalMultiControlRuntime.SwitchControlledPlayerTo(nextPlayerId.Value, "rest-site-next-mandatory-choice");
        }).CallDeferred();
        LocalMultiControlLogger.Info(
            $"休息区选择成功后切换到下一位待选角色: {localPlayerId.Value} -> {nextPlayerId.Value}, optionIndex={optionIndex}, snapshot={DescribeOptions(sourceOptionsSnapshot)}");

        return success;
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

[HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom._Ready))]
internal static class NRestSiteRoomReadyPatch
{
    [HarmonyPostfix]
    private static void Postfix(NRestSiteRoom __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        Callable.From(delegate
        {
            EnsurePrimaryPlayerOptionsVisible(__instance, attempt: 0);
        }).CallDeferred();
    }

    private static void EnsurePrimaryPlayerOptionsVisible(NRestSiteRoom room, int attempt)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        if (NRestSiteRoom.Instance != room)
        {
            return;
        }

        LocalMultiControlRuntime.SwitchControlledPlayerTo(LocalSelfCoopContext.PrimaryPlayerId, $"rest-site-enter-primary-{attempt}");
        RestSiteUiRefreshUtil.TryRefresh($"rest-site-enter-primary-{attempt}");

        int optionCount = room.Options.Count;
        if (optionCount > 0 || attempt >= 3)
        {
            LocalMultiControlLogger.Info($"休息区进入后选项检查: attempt={attempt}, options={optionCount}");
            return;
        }

        Callable.From(delegate
        {
            EnsurePrimaryPlayerOptionsVisible(room, attempt + 1);
        }).CallDeferred();
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
