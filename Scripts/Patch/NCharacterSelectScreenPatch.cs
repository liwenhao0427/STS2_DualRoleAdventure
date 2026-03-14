using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
internal static class NCharacterSelectScreenOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        LocalSelfCoopContext.ActiveCharacterSelectScreen = __instance;
        LocalSelfCoopContext.EnsureLobbySenderContext("character-select-opened");
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
internal static class NCharacterSelectScreenSelectCharacterPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        LocalSelfCoopContext.EnsureLobbySenderContext("character-select-before-select");
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.PlayerChanged))]
internal static class NCharacterSelectScreenPatch
{
    [HarmonyPostfix]
    private static void Postfix(LobbyPlayer player)
    {
        LocalSelfCoopContext.NotifyCharacterSelectPlayerChanged(player.id);
    }
}
