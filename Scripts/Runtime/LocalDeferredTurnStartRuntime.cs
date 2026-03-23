using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalDeferredTurnStartRuntime
{
    private readonly struct PendingEntropyKey : IEquatable<PendingEntropyKey>
    {
        public PendingEntropyKey(int combatIdentity, int round, ulong playerId)
        {
            CombatIdentity = combatIdentity;
            Round = round;
            PlayerId = playerId;
        }

        public int CombatIdentity { get; }

        public int Round { get; }

        public ulong PlayerId { get; }

        public bool Equals(PendingEntropyKey other)
        {
            return CombatIdentity == other.CombatIdentity
                && Round == other.Round
                && PlayerId == other.PlayerId;
        }

        public override bool Equals(object? obj)
        {
            return obj is PendingEntropyKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(CombatIdentity, Round, PlayerId);
        }
    }

    private sealed class PendingEntropyChoice
    {
        public required EntropyPower Power { get; init; }

        public required Player Player { get; init; }
    }

    private static readonly Dictionary<PendingEntropyKey, PendingEntropyChoice> PendingEntropyChoices = new();
    private static readonly HashSet<PendingEntropyKey> InFlightEntropyKeys = new();
    private const int EntropySwitchMaxRetry = 5;
    private const int EntropySwitchRetryDelayMs = 50;

    public static void QueueEntropyChoice(EntropyPower power, Player player)
    {
        CombatState? combatState = player.Creature.CombatState;
        if (combatState == null)
        {
            return;
        }

        PendingEntropyKey key = new(RuntimeHelpers.GetHashCode(combatState), combatState.RoundNumber, player.NetId);
        PendingEntropyChoices[key] = new PendingEntropyChoice
        {
            Power = power,
            Player = player
        };
        LocalMultiControlLogger.Info(
            $"已挂起熵的手选触发，等待切回角色执行: player={player.NetId}, round={combatState.RoundNumber}, amount={power.Amount}");
    }

    public static void QueueEntropyChoiceAndSwitchToOwner(EntropyPower power, Player player, string source)
    {
        CombatState? combatState = player.Creature.CombatState;
        if (combatState == null)
        {
            return;
        }

        QueueEntropyChoice(power, player);
        LocalMultiControlRuntime.SwitchControlledPlayerTo(player.NetId, $"entropy-owner-{source}");
        TryRunPendingEntropyForControlledPlayer(player.NetId, $"entropy-owner-{source}");
    }

    public static void TryRunPendingEntropyForControlledPlayer(ulong playerId, string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        Player? player = runState?.GetPlayer(playerId);
        CombatState? combatState = player?.Creature?.CombatState;
        if (combatState == null || combatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        PendingEntropyKey key = new(RuntimeHelpers.GetHashCode(combatState), combatState.RoundNumber, playerId);
        if (!PendingEntropyChoices.TryGetValue(key, out PendingEntropyChoice? pendingChoice))
        {
            return;
        }

        if (!InFlightEntropyKeys.Add(key))
        {
            return;
        }

        TaskHelper.RunSafely(RunPendingEntropyChoiceAsync(key, pendingChoice, source));
    }

    private static async Task RunPendingEntropyChoiceAsync(
        PendingEntropyKey key,
        PendingEntropyChoice pendingChoice,
        string source)
    {
        try
        {
            if (!RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
            {
                return;
            }

            Player player = pendingChoice.Player;
            EntropyPower power = pendingChoice.Power;
            CombatState? combatState = player.Creature.CombatState;
            if (combatState == null)
            {
                return;
            }

            if (RuntimeHelpers.GetHashCode(combatState) != key.CombatIdentity || combatState.RoundNumber != key.Round)
            {
                return;
            }

            if (power.Owner?.Player != player || power.Amount <= 0)
            {
                return;
            }

            bool ownerContextReady = await EnsureEntropyOwnerContextReadyAsync(player, source, combatState.RoundNumber);
            if (!ownerContextReady)
            {
                LocalMultiControlLogger.Warn(
                    $"熵手选上下文校验失败，取消本次执行: player={player.NetId}, round={combatState.RoundNumber}, source={source}");
                return;
            }

            HookPlayerChoiceContext choiceContext = new HookPlayerChoiceContext(
                power,
                player.NetId,
                combatState,
                GameActionType.Combat);

            Task action = ExecuteEntropyChoiceAsync(power, choiceContext, player);
            await choiceContext.AssignTaskAndWaitForPauseOrCompletion(action);
            await action;
            LocalMultiControlLogger.Info(
                $"已执行挂起的熵手选触发: player={player.NetId}, round={combatState.RoundNumber}, source={source}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"执行挂起熵手选失败: player={key.PlayerId}, round={key.Round}, error={exception.Message}");
        }
        finally
        {
            InFlightEntropyKeys.Remove(key);
            PendingEntropyChoices.Remove(key);
        }
    }

    private static async Task ExecuteEntropyChoiceAsync(EntropyPower power, PlayerChoiceContext choiceContext, Player player)
    {
        CardSelectorPrefs prefs = new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, power.Amount);
        List<CardModel> cards = (await CardSelectCmd.FromHand(choiceContext, player, prefs, null, power)).ToList();
        foreach (CardModel card in cards)
        {
            await CardCmd.TransformToRandom(card, player.RunState.Rng.CombatCardSelection);
        }
    }

    private static async Task<bool> EnsureEntropyOwnerContextReadyAsync(Player player, string source, int round)
    {
        for (int attempt = 1; attempt <= EntropySwitchMaxRetry; attempt++)
        {
            if (!RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
            {
                return false;
            }

            LocalMultiControlRuntime.SwitchControlledPlayerTo(player.NetId, $"entropy-align-{source}-try{attempt}");
            LocalMultiControlRuntime.AlignContextForActionOwner(player.NetId, $"entropy-align-{source}-try{attempt}");

            if (IsEntropyHandContextReady(player))
            {
                return true;
            }

            LocalMultiControlLogger.Warn(
                $"熵手选前检测到手牌上下文未对齐，准备重试切换: player={player.NetId}, round={round}, source={source}, attempt={attempt}/{EntropySwitchMaxRetry}");
            await Task.Delay(EntropySwitchRetryDelayMs);
        }

        return IsEntropyHandContextReady(player);
    }

    private static bool IsEntropyHandContextReady(Player player)
    {
        if (!RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return false;
        }

        ulong currentControlledPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId
            ?? LocalContext.NetId
            ?? 0UL;
        if (currentControlledPlayerId != player.NetId || LocalContext.NetId != player.NetId)
        {
            return false;
        }

        NPlayerHand? hand = NCombatRoom.Instance?.Ui?.Hand;
        if (hand == null || hand.InCardPlay || hand.IsInCardSelection)
        {
            return false;
        }

        List<CardModel> expectedHandCards = PileType.Hand.GetPile(player).Cards.ToList();
        if (expectedHandCards.Count == 0)
        {
            return true;
        }

        List<CardModel> visibleHandCards = hand.ActiveHolders
            .Select((holder) => holder.CardModel)
            .Where((card) => card != null)
            .Cast<CardModel>()
            .ToList();
        if (visibleHandCards.Count == 0)
        {
            return false;
        }

        return visibleHandCards.All((card) => card.Owner.NetId == player.NetId);
    }
}
