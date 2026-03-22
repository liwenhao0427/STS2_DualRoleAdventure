using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Models.Relics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using Godot;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalWakuuRelicRuntime
{
    private const int MaxCardsToPlay = 13;
    private const long WatchdogRestartCooldownMs = 300L;

    private static readonly Dictionary<string, long> _watchdogLastRunAt = new();
    private static readonly HashSet<string> _watchdogInFlight = new();

    public static LocalWakuuStarterRelic? TryGetWakuuRelic(Player player)
    {
        return player.GetRelicById(ModelDb.GetId<LocalWakuuStarterRelic>()) as LocalWakuuStarterRelic;
    }

    public static bool HasWakuuRelic(Player player)
    {
        return TryGetWakuuRelic(player) != null;
    }

    public static async Task ExecuteBeforePlayPhaseStartAsync(
        LocalWakuuStarterRelic relic,
        PlayerChoiceContext choiceContext,
        Player player)
    {
        if (!LocalSelfCoopContext.IsEnabled || player != relic.Owner)
        {
            return;
        }

        CombatState? combatState = player.Creature.CombatState;
        if (combatState == null || CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

        CardModel? firstPlayableCard = PileType.Hand.GetPile(relic.Owner).Cards.FirstOrDefault((candidate) => candidate.CanPlay());
        if (firstPlayableCard == null)
        {
            return;
        }

        EnsureWakuuPerspective(player, "before-play-phase");
        relic.Flash();

        bool reachedPlayLimit;
        int cardsPlayed;
        using (CardSelectCmd.PushSelector(new VakuuCardSelector()))
        {
            for (cardsPlayed = 0; cardsPlayed < MaxCardsToPlay; cardsPlayed++)
            {
                if (CombatManager.Instance.IsOverOrEnding)
                {
                    break;
                }

                CardModel? card = cardsPlayed == 0
                    ? firstPlayableCard
                    : PileType.Hand.GetPile(relic.Owner).Cards.FirstOrDefault((candidate) => candidate.CanPlay());
                if (card == null)
                {
                    break;
                }

                Creature? target = ResolveTarget(card, combatState, relic.Owner);
                await card.SpendResources();
                await CardCmd.AutoPlay(choiceContext, card, target, AutoPlayType.Default, skipXCapture: true);
            }

            reachedPlayLimit = cardsPlayed >= MaxCardsToPlay;
        }

        if (cardsPlayed <= 0)
        {
            return;
        }

        LocString line = reachedPlayLimit
            ? new LocString("relics", "WHISPERING_EARRING.warning")
            : new LocString("relics", "WHISPERING_EARRING.approval");
        TalkCmd.Play(line, relic.Owner.Creature);
    }

    public static bool TryScheduleWatchdog(Player player, string source)
    {
        if (LocalManualPlayGuard.IsActive)
        {
            return false;
        }

        CombatState? combatState = player.Creature.CombatState;
        if (combatState == null || combatState.CurrentSide != CombatSide.Player || CombatManager.Instance.IsOverOrEnding)
        {
            return false;
        }

        LocalWakuuStarterRelic? relic = TryGetWakuuRelic(player);
        if (relic == null)
        {
            return false;
        }

        bool hasPlayableCards = PileType.Hand.GetPile(player).Cards.Any((card) => card.CanPlay());
        if (!hasPlayableCards)
        {
            return false;
        }

        string key = $"{combatState.RoundNumber}:{player.NetId}";
        long nowMs = (long)Time.GetTicksMsec();
        if (_watchdogInFlight.Contains(key))
        {
            return false;
        }

        if (_watchdogLastRunAt.TryGetValue(key, out long lastRunMs)
            && nowMs - lastRunMs < WatchdogRestartCooldownMs)
        {
            return false;
        }

        _watchdogLastRunAt[key] = nowMs;
        _watchdogInFlight.Add(key);
        TaskHelper.RunSafely(RunWatchdogAsync(key, relic, player, combatState, source));
        return true;
    }

    private static async Task RunWatchdogAsync(
        string key,
        LocalWakuuStarterRelic relic,
        Player player,
        CombatState combatState,
        string source)
    {
        ulong? previousNetId = LocalContext.NetId;
        ulong previousSenderId = LocalSelfCoopContext.NetService?.NetId ?? 0UL;
        bool hasNetService = LocalSelfCoopContext.NetService != null;
        try
        {
            if (LocalManualPlayGuard.IsActive)
            {
                return;
            }

            if (!RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress || CombatManager.Instance.IsOverOrEnding)
            {
                return;
            }

            if (player.Creature.CombatState != combatState || !HasWakuuRelic(player))
            {
                return;
            }

            if (!PileType.Hand.GetPile(player).Cards.Any((card) => card.CanPlay()))
            {
                return;
            }

            EnsureWakuuPerspective(player, source);
            LocalContext.NetId = player.NetId;
            LocalSelfCoopContext.NetService?.SetCurrentSenderId(player.NetId);

            HookPlayerChoiceContext choiceContext = new HookPlayerChoiceContext(
                relic,
                player.NetId,
                combatState,
                GameActionType.CombatPlayPhaseOnly);
            Task action = ExecuteBeforePlayPhaseStartAsync(relic, choiceContext, player);
            await choiceContext.AssignTaskAndWaitForPauseOrCompletion(action);
            await action;
            LocalMultiControlLogger.Info($"瓦库看门狗已重启自动出牌: player={player.NetId}, source={source}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库看门狗重启失败: player={player.NetId}, source={source}, error={exception.Message}");
        }
        finally
        {
            _watchdogInFlight.Remove(key);
            LocalContext.NetId = previousNetId;
            if (hasNetService)
            {
                LocalSelfCoopContext.NetService?.SetCurrentSenderId(previousSenderId);
            }

            Callable.From(delegate
            {
                LocalMultiControlRuntime.RequestAutoSwitchToNonWakuuOncePerRound($"wakuu-watchdog-{source}");
            }).CallDeferred();
        }
    }

    private static Creature? ResolveTarget(CardModel card, CombatState combatState, Player owner)
    {
        return card.TargetType switch
        {
            TargetType.AnyEnemy => combatState.HittableEnemies.FirstOrDefault(),
            TargetType.AnyAlly => owner.RunState.Rng.CombatTargets.NextItem(
                combatState.Allies.Where((creature) => creature != null && creature.IsAlive && creature.IsPlayer && creature != owner.Creature)),
            TargetType.AnyPlayer => owner.Creature,
            _ => null
        };
    }

    private static void EnsureWakuuPerspective(Player player, string source)
    {
        ulong currentControlledPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId
            ?? LocalContext.NetId
            ?? player.NetId;
        if (currentControlledPlayerId == player.NetId)
        {
            return;
        }

        LocalMultiControlLogger.Info($"瓦库自动操作前切换视角: {currentControlledPlayerId} -> {player.NetId}, source={source}");
        LocalMultiControlRuntime.SwitchControlledPlayerTo(player.NetId, $"wakuu-{source}");
    }
}
