using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private static int _autoSmithSelectionFlag;

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
        bool chooseFromSmith = localPlayerId.HasValue && IsSmithOption(synchronizer, localPlayerId.Value, optionIndex);
        bool canChain = Volatile.Read(ref _autoSmithSelectionFlag) == 0;

        bool success = await originalTask;
        if (!success || !chooseFromSmith || !canChain)
        {
            return success;
        }

        if (Interlocked.CompareExchange(ref _autoSmithSelectionFlag, 1, 0) != 0)
        {
            return success;
        }

        try
        {
            await TryRunSecondSmithFlowAsync(synchronizer, localPlayerId!.Value);
        }
        finally
        {
            Volatile.Write(ref _autoSmithSelectionFlag, 0);
        }

        return success;
    }

    private static bool IsSmithOption(RestSiteSynchronizer synchronizer, ulong playerId, int optionIndex)
    {
        IReadOnlyList<RestSiteOption> options = synchronizer.GetOptionsForPlayer(playerId);
        if (optionIndex < 0 || optionIndex >= options.Count)
        {
            return false;
        }

        return options[optionIndex] is SmithRestSiteOption;
    }

    private static async Task TryRunSecondSmithFlowAsync(RestSiteSynchronizer synchronizer, ulong sourcePlayerId)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null || runState.Players.Count != 2)
        {
            return;
        }

        Player? otherPlayer = runState.Players.FirstOrDefault((candidate) => candidate.NetId != sourcePlayerId);
        if (otherPlayer == null)
        {
            return;
        }

        IReadOnlyList<RestSiteOption> existingOptions = synchronizer.GetOptionsForPlayer(otherPlayer.NetId);
        int smithOptionIndex = FindSmithOptionIndex(existingOptions);
        if (smithOptionIndex < 0)
        {
            LocalMultiControlLogger.Info($"休息区升级串行跳过：另一名角色无可用升级选项。player={otherPlayer.NetId}");
            return;
        }

        LocalMultiControlLogger.Info($"休息区升级串行开始：{sourcePlayerId} -> {otherPlayer.NetId}");
        LocalMultiControlRuntime.SwitchControlledPlayerTo(otherPlayer.NetId, "rest-site-auto-smith");
        bool secondSelectionSuccess = await synchronizer.ChooseLocalOption(smithOptionIndex);
        LocalMultiControlLogger.Info($"休息区升级串行结束：player={otherPlayer.NetId}, success={secondSelectionSuccess}");
    }

    private static int FindSmithOptionIndex(IReadOnlyList<RestSiteOption> options)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is SmithRestSiteOption)
            {
                return i;
            }
        }

        return -1;
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
