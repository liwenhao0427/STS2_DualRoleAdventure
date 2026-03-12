using System.Linq;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalSelfCoopContext
{
    public const ulong PrimaryPlayerId = 1;

    public const ulong SecondaryPlayerId = 2;

    public static bool IsEnabled { get; private set; }

    public static LocalLoopbackHostGameService? NetService { get; private set; }

    public static void Enable(LocalLoopbackHostGameService netService)
    {
        IsEnabled = true;
        NetService = netService;
        LocalMultiControlLogger.Info("本地双人联机模式已启用。");
    }

    public static void Disable(string reason)
    {
        if (!IsEnabled && NetService == null)
        {
            return;
        }

        LocalMultiControlLogger.Info($"本地双人联机模式已关闭，原因: {reason}");
        IsEnabled = false;
        NetService = null;
    }

    public static bool BootstrapSecondPlayer(NCharacterSelectScreen characterSelectScreen)
    {
        if (!IsEnabled || NetService == null)
        {
            LocalMultiControlLogger.Warn("尝试注入第二本地玩家失败：本地双人联机模式未启用。");
            return false;
        }

        if (characterSelectScreen.Lobby.Players.Count >= 2)
        {
            LocalMultiControlLogger.Info("角色选择大厅已存在2名及以上玩家，跳过注入第二本地玩家。");
            return true;
        }

        UnlockState unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
        int maxAscension = SaveManager.Instance.Progress.MaxMultiplayerAscension;

        NetService.SetCurrentSenderId(SecondaryPlayerId);
        SerializableUnlockState serializableUnlockState = unlockState.ToSerializable();
        _ = characterSelectScreen.Lobby.AddLocalHostPlayerInternal(serializableUnlockState, maxAscension);
        NetService.SetCurrentSenderId(PrimaryPlayerId);

        int secondPlayerIndex = characterSelectScreen.Lobby.Players.FindIndex((player) => player.id == SecondaryPlayerId);
        if (secondPlayerIndex < 0)
        {
            LocalMultiControlLogger.Error("注入第二本地玩家失败：未在 Lobby 中找到 playerId=2。");
            return false;
        }

        LobbyPlayer secondPlayer = characterSelectScreen.Lobby.Players[secondPlayerIndex];
        secondPlayer.isReady = true;
        characterSelectScreen.Lobby.Players[secondPlayerIndex] = secondPlayer;
        characterSelectScreen.PlayerChanged(secondPlayer);

        LocalMultiControlLogger.Info($"初始化2人本地队伍成功。玩家: {string.Join(",", characterSelectScreen.Lobby.Players.Select((player) => player.id))}");
        return true;
    }
}
