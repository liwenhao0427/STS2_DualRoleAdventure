using System.Runtime.InteropServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using LocalMultiControl.Scripts.Runtime;

namespace LocalMultiControl.Scripts;

[ModInitializer(nameof(Init))]
public partial class Entry
{
    private const string BuildMarker = "RestSiteRecoveryV2 loaded (marker=2026-03-20-r1)";

    private static Harmony? _harmony;

    /*/
    On Linux, Harmony extracts a small native helper (mm-exhelper.so) and loads it at runtime. 
    That helper needs a function from libgcc_s, but the .NET runtime loads libgcc_s privately so the function isn't visible to other libraries.
    Loading libgcc_s ourselves first (globally) makes it visible before Harmony needs it. 
    /*/
    [DllImport("libdl.so.2")]
    private static extern nint dlopen(string? filename, int flags);

    private const int RtldNow = 0x2;
    private const int RtldGlobal = 0x100;

    public static void Init()
    {
        LocalMultiControlLogger.Info("开始初始化 Harmony 补丁。");
        LocalMultiControlLogger.Info(BuildMarker);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            dlopen("libgcc_s.so.1", RtldNow | RtldGlobal);
        }

        LocalWakuuRelicLocalization.Initialize();
        _harmony = new Harmony("sts2.dualroleadventure");
        _harmony.PatchAll();
        LocalMultiControlLogger.Info("Mod 初始化完成。");
    }
}
