using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.PlayerChanged))]
internal static class NCharacterSelectScreenPatch
{
    [HarmonyPostfix]
    private static void Postfix(LobbyPlayer player)
    {
        LocalSelfCoopContext.NotifyCharacterSelectPlayerChanged(player.id);
    }
}
