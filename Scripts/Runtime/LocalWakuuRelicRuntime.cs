using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalWakuuRelicRuntime
{
    private const int MaxCardsToPlay = 13;

    public static async Task ExecuteBeforePlayPhaseStartAsync(
        WhisperingEarring relic,
        PlayerChoiceContext choiceContext,
        Player player)
    {
        if (player != relic.Owner)
        {
            return;
        }

        CombatState? combatState = player.Creature.CombatState;
        if (combatState == null || CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

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

                CardPile handPile = PileType.Hand.GetPile(relic.Owner);
                CardModel? card = handPile.Cards.FirstOrDefault((candidate) => candidate.CanPlay());
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
}
