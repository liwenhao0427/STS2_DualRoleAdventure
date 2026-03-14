using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCombatRoom), "OnCombatSetUp")]
internal static class NCombatRoomPatch
{
    [HarmonyPostfix]
    private static void Postfix(CombatState state)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        LocalMultiControlRuntime.SwitchControlledPlayerTo(LocalSelfCoopContext.PrimaryPlayerId, "combat-setup");
        LocalMultiControlRuntime.RefreshSharedTopBarForCombat("combat-setup");
        Callable.From(delegate
        {
            LocalMultiControlRuntime.RefreshSharedTopBarForCombat("combat-setup-deferred");
        }).CallDeferred();
    }
}
