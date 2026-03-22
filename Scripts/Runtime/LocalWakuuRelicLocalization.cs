using System;
using System.Collections.Generic;
using LocalMultiControl.Scripts.Models.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalWakuuRelicLocalization
{
    private static bool _localeCallbackSubscribed;

    public static void Initialize()
    {
        InjectLocalization();
        if (_localeCallbackSubscribed)
        {
            return;
        }

        LocManager.Instance.SubscribeToLocaleChange(InjectLocalization);
        _localeCallbackSubscribed = true;
    }

    private static void InjectLocalization()
    {
        try
        {
            string entry = ModelDb.GetId<LocalWakuuStarterRelic>().Entry;
            Dictionary<string, string> customEntries = new()
            {
                [$"{entry}.title"] = LocalModText.Select("瓦库接管印记", "Vakuu Control Sigil"),
                [$"{entry}.description"] = LocalModText.Select(
                    "本地多控专用遗物。拥有者会在战斗中由瓦库自动接管操作。",
                    "Local multi-control relic. Its owner is auto-controlled by Vakuu in combat."),
                [$"{entry}.flavor"] = LocalModText.Select(
                    "它不会低语，只会稳稳地接住你的回合。",
                    "It does not whisper. It simply holds your turn steady.")
            };

            LocManager.Instance.GetTable("relics").MergeWith(customEntries);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注入瓦库专用遗物本地化失败: {exception.Message}");
        }
    }
}
