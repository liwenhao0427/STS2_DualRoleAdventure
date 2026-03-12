using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace LocalMultiControl.Scripts;

[ModInitializer(nameof(Init))]
public partial class Entry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        _harmony = new Harmony("sts2.localmulticontrol");
        _harmony.PatchAll();
        Log.Info("[LocalMultiControl] Mod initialized");
    }
}
