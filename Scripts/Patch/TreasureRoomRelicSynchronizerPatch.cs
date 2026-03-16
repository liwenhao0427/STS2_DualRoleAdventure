using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked))]
internal static class TreasureRoomRelicSynchronizerPatch
{
    internal sealed class OverflowCopyPlan
    {
        public int ManualPlayerCount { get; init; }

        public List<(Player player, int relicIndex)> OverflowRecipients { get; } = new();
    }

    private static readonly Dictionary<TreasureRoomRelicSynchronizer, OverflowCopyPlan> OverflowPlans = new();

    [HarmonyPostfix]
    private static void Postfix(TreasureRoomRelicSynchronizer __instance, Player player)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        TryAutoSwitchToNextUnpickedPlayer(__instance, player.NetId, "treasure-picked");
    }

    [HarmonyPrefix]
    private static bool Prefix(TreasureRoomRelicSynchronizer __instance, Player player, int index)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (!TryGetOverflowPlan(__instance, out OverflowCopyPlan? plan) || plan == null)
        {
            return true;
        }

        try
        {
            List<int?>? votes = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes")?.GetValue(__instance) as List<int?>;
            IPlayerCollection? players = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection")?.GetValue(__instance) as IPlayerCollection;
            IReadOnlyList<RelicModel>? currentRelics = __instance.CurrentRelics;
            if (votes == null || players == null || currentRelics == null || currentRelics.Count == 0)
            {
                return false;
            }

            if (index < 0 || index >= currentRelics.Count)
            {
                LocalMultiControlLogger.Warn($"宝箱投票索引越界，已忽略: index={index}, relicCount={currentRelics.Count}");
                return false;
            }

            int playerSlot = players.GetPlayerSlotIndex(player);
            int manualPlayerCount = Math.Min(plan.ManualPlayerCount, Math.Min(players.Players.Count, votes.Count));
            if (playerSlot >= manualPlayerCount)
            {
                LocalMultiControlLogger.Info($"宝箱后续角色已跳过投票: player={player.NetId}, slot={playerSlot}");
                return false;
            }

            votes[playerSlot] = index;
            InvokeVotesChanged(__instance);

            bool allManualPicked = true;
            for (int i = 0; i < manualPlayerCount; i++)
            {
                if (!votes[i].HasValue)
                {
                    allManualPicked = false;
                    break;
                }
            }

            if (!allManualPicked)
            {
                return false;
            }

            List<RelicPickingResult> results = BuildManualPickingResults(players, votes, currentRelics, manualPlayerCount, __instance);
            InvokeRelicsAwarded(__instance, results);
            GrantOverflowCopies(plan, currentRelics);
            AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), "EndRelicVoting")?.Invoke(__instance, null);
            RemoveOverflowPlan(__instance);
            return false;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"宝箱溢出投票接管失败，回退原流程: {exception.Message}");
            RemoveOverflowPlan(__instance);
            return true;
        }
    }

    private static List<RelicPickingResult> BuildManualPickingResults(
        IPlayerCollection players,
        List<int?> votes,
        IReadOnlyList<RelicModel> currentRelics,
        int manualPlayerCount,
        TreasureRoomRelicSynchronizer synchronizer)
    {
        Dictionary<int, List<Player>> groupedVotes = new();
        for (int i = 0; i < manualPlayerCount; i++)
        {
            groupedVotes[i] = new List<Player>();
        }

        for (int i = 0; i < manualPlayerCount; i++)
        {
            int voteIndex = votes[i] ?? 0;
            if (voteIndex < 0 || voteIndex >= manualPlayerCount)
            {
                voteIndex = Math.Clamp(voteIndex, 0, manualPlayerCount - 1);
            }

            groupedVotes[voteIndex].Add(players.Players[i]);
        }

        Rng? rng = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_rng")?.GetValue(synchronizer) as Rng;
        RelicPickingFightMove[] possibleMoves = Enum.GetValues<RelicPickingFightMove>();
        List<RelicPickingResult> results = new();
        List<RelicModel> leftovers = new();
        foreach (KeyValuePair<int, List<Player>> entry in groupedVotes)
        {
            RelicModel relic = currentRelics[entry.Key];
            if (entry.Value.Count == 0)
            {
                leftovers.Add(relic);
            }
            else if (entry.Value.Count == 1)
            {
                results.Add(new RelicPickingResult
                {
                    type = RelicPickingResultType.OnlyOnePlayerVoted,
                    relic = relic,
                    player = entry.Value[0]
                });
            }
            else
            {
                results.Add(RelicPickingResult.GenerateRelicFight(
                    entry.Value,
                    relic,
                    () => rng?.NextItem(possibleMoves) ?? possibleMoves[0]));
            }
        }

        List<Player> noPrizePlayers = players.Players
            .Take(manualPlayerCount)
            .Where((candidate) => results.All((result) => result.player != candidate))
            .ToList();
        if (rng != null)
        {
            leftovers.StableShuffle(rng);
        }

        for (int i = 0; i < Math.Min(leftovers.Count, noPrizePlayers.Count); i++)
        {
            results.Add(new RelicPickingResult
            {
                type = RelicPickingResultType.ConsolationPrize,
                player = noPrizePlayers[i],
                relic = leftovers[i]
            });
        }

        return results;
    }

    private static void GrantOverflowCopies(OverflowCopyPlan plan, IReadOnlyList<RelicModel> currentRelics)
    {
        foreach ((Player player, int relicIndex) in plan.OverflowRecipients)
        {
            if (relicIndex < 0 || relicIndex >= currentRelics.Count)
            {
                continue;
            }

            RelicModel copiedRelic = currentRelics[relicIndex].ToMutable();
            TaskHelper.RunSafely(RelicCmd.Obtain(copiedRelic, player));
            LocalMultiControlLogger.Info($"宝箱后续角色直接复制遗物: player={player.NetId}, relic={copiedRelic.Id.Entry}, fromIndex={relicIndex}");
        }
    }

    private static void InvokeVotesChanged(TreasureRoomRelicSynchronizer synchronizer)
    {
        Action? callback = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "VotesChanged")?.GetValue(synchronizer) as Action;
        callback?.Invoke();
    }

    private static void InvokeRelicsAwarded(TreasureRoomRelicSynchronizer synchronizer, List<RelicPickingResult> results)
    {
        Action<List<RelicPickingResult>>? callback =
            AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "RelicsAwarded")?.GetValue(synchronizer) as Action<List<RelicPickingResult>>;
        callback?.Invoke(results);
    }

    internal static void SetOverflowPlan(TreasureRoomRelicSynchronizer synchronizer, OverflowCopyPlan plan)
    {
        OverflowPlans[synchronizer] = plan;
    }

    private static bool TryGetOverflowPlan(TreasureRoomRelicSynchronizer synchronizer, out OverflowCopyPlan? plan)
    {
        return OverflowPlans.TryGetValue(synchronizer, out plan);
    }

    internal static void RemoveOverflowPlan(TreasureRoomRelicSynchronizer synchronizer)
    {
        OverflowPlans.Remove(synchronizer);
    }

    internal static bool TryAutoSwitchToNextUnpickedPlayer(TreasureRoomRelicSynchronizer synchronizer, ulong currentPlayerId, string source)
    {
        try
        {
            List<int?>? votes = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes")?.GetValue(synchronizer) as List<int?>;
            IPlayerCollection? players = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection")?.GetValue(synchronizer) as IPlayerCollection;
            if (votes == null || players == null)
            {
                return false;
            }

            int sharedCount = Math.Min(votes.Count, players.Players.Count);
            if (sharedCount < 2)
            {
                return false;
            }

            int currentSlot = players.Players.Take(sharedCount).ToList().FindIndex((candidate) => candidate.NetId == currentPlayerId);
            if (currentSlot < 0)
            {
                return false;
            }

            if (!votes[currentSlot].HasValue)
            {
                return false;
            }

            int nextSlot = -1;
            for (int step = 1; step < sharedCount; step++)
            {
                int slot = (currentSlot + step) % sharedCount;
                if (!votes[slot].HasValue)
                {
                    nextSlot = slot;
                    break;
                }
            }

            if (nextSlot < 0)
            {
                return false;
            }

            ulong nextPlayerId = players.Players[nextSlot].NetId;
            Callable.From(delegate
            {
                LocalMultiControlRuntime.SwitchControlledPlayerTo(nextPlayerId, source);
            }).CallDeferred();

            LocalMultiControlLogger.Info($"宝箱选择完成后自动切换到下一位未选角色: {currentPlayerId} -> {nextPlayerId}");
            return true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"宝箱自动切换未选角色失败: {exception.Message}");
            return false;
        }
    }
}

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
internal static class TreasureRoomRelicSynchronizerBeginPatch
{
    private const int MAX_MANUAL_RELIC_OPTIONS = 4;
    private const int SKIPPED_VOTE_SENTINEL = -1;

    [HarmonyPostfix]
    private static void Postfix(TreasureRoomRelicSynchronizer __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        try
        {
            List<int?>? votes = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes")?.GetValue(__instance) as List<int?>;
            IReadOnlyList<Player>? players = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection")?.GetValue(__instance) is IPlayerCollection playerCollection
                ? playerCollection.Players
                : null;
            IReadOnlyList<RelicModel>? currentRelics = __instance.CurrentRelics;
            if (votes == null || players == null || votes.Count <= 1 || currentRelics == null || currentRelics.Count <= 1)
            {
                return;
            }

            int sharedCount = Math.Min(votes.Count, players.Count);
            int manualPlayerCount = Math.Min(sharedCount, Math.Min(currentRelics.Count, MAX_MANUAL_RELIC_OPTIONS));
            if (manualPlayerCount <= 0)
            {
                return;
            }

            int autoCopyCount = sharedCount - manualPlayerCount;
            if (autoCopyCount > 0)
            {
                TreasureRoomRelicSynchronizerPatch.OverflowCopyPlan plan = new()
                {
                    ManualPlayerCount = manualPlayerCount
                };

                Rng? rng = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_rng")?.GetValue(__instance) as Rng;
                for (int i = 0; i < sharedCount; i++)
                {
                    if (i < manualPlayerCount)
                    {
                        votes[i] = null;
                    }
                    else
                    {
                        int pickIndex = rng?.NextInt(manualPlayerCount) ?? 0;
                        votes[i] = SKIPPED_VOTE_SENTINEL;
                        plan.OverflowRecipients.Add((players[i], pickIndex));
                    }
                }

                TreasureRoomRelicSynchronizerPatch.SetOverflowPlan(__instance, plan);
                LocalMultiControlLogger.Info($"宝箱候选遗物超过界面上限，已启用后续角色随机复制: manual={manualPlayerCount}, autoCopy={autoCopyCount}");
            }
            else
            {
                for (int i = 0; i < sharedCount; i++)
                {
                    votes[i] = null;
                }

                TreasureRoomRelicSynchronizerPatch.RemoveOverflowPlan(__instance);
                LocalMultiControlLogger.Info("宝箱已禁用自动代投，改为逐角色手动选择。");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"禁用宝箱自动代投失败: {exception.Message}");
        }
    }
}
