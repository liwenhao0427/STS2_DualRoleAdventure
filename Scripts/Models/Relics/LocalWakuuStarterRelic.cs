using System.Threading.Tasks;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Models.Relics;

internal sealed class LocalWakuuStarterRelic : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Event;

    protected override string IconBaseName => "whispering_earring";

    public override Task BeforePlayPhaseStart(PlayerChoiceContext choiceContext, Player player)
    {
        return LocalWakuuRelicRuntime.ExecuteBeforePlayPhaseStartAsync(this, choiceContext, player);
    }
}
