using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Multiplayer.Quality;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

internal sealed class LocalLoopbackHostGameService : INetHostGameService
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

    private readonly List<NetClientData> _connectedPeers = new List<NetClientData>();

    private ulong _currentSenderId;

    public LocalLoopbackHostGameService(ulong hostPlayerId)
    {
        _currentSenderId = hostPlayerId;
        IsConnected = true;
        LocalMultiControlLogger.Info($"创建本地回环网络服务，初始 sender: {_currentSenderId}");
    }

    public ulong NetId => _currentSenderId;

    public bool IsConnected { get; private set; }

    public bool IsGameLoading { get; private set; }

    public NetGameType Type => NetGameType.Host;

    public PlatformType Platform => PlatformType.None;

    public IReadOnlyList<NetClientData> ConnectedPeers => _connectedPeers;

    public NetHost? NetHost => null;

    public event Action<NetErrorInfo>? Disconnected;

    public event Action<ulong>? ClientConnected;

    public event Action<ulong, NetErrorInfo>? ClientDisconnected;

    public void SetCurrentSenderId(ulong playerId)
    {
        if (_currentSenderId == playerId)
        {
            return;
        }

        LocalMultiControlLogger.Info($"sender切换: {_currentSenderId} -> {playerId}");
        _currentSenderId = playerId;
    }

    public void SendMessage<T>(T message, ulong playerId) where T : INetMessage
    {
        LocalMultiControlLogger.Info($"本地回环定向发送消息: {typeof(T).Name}, sender={_currentSenderId}, target={playerId}");
    }

    public void SendMessage<T>(T message) where T : INetMessage
    {
        if (message is not PeerInputMessage)
        {
            LocalMultiControlLogger.Info($"本地回环广播消息: {typeof(T).Name}, sender={_currentSenderId}");
        }

        TryDispatchSyntheticSecondPlayerSync(message);
    }

    public void RegisterMessageHandler<T>(MessageHandlerDelegate<T> messageHandlerDelegate) where T : INetMessage
    {
        Type messageType = typeof(T);
        if (!_handlers.TryGetValue(messageType, out List<Delegate>? handlers))
        {
            handlers = new List<Delegate>();
            _handlers[messageType] = handlers;
        }

        handlers.Add(messageHandlerDelegate);
    }

    public void UnregisterMessageHandler<T>(MessageHandlerDelegate<T> messageHandlerDelegate) where T : INetMessage
    {
        Type messageType = typeof(T);
        if (_handlers.TryGetValue(messageType, out List<Delegate>? handlers))
        {
            handlers.Remove(messageHandlerDelegate);
        }
    }

    public void DispatchLoopback<T>(T message, ulong senderId) where T : INetMessage
    {
        Type messageType = typeof(T);
        if (!_handlers.TryGetValue(messageType, out List<Delegate>? handlers) || handlers.Count == 0)
        {
            LocalMultiControlLogger.Info($"本地回环消息分发: {messageType.Name}, sender={senderId}, handlers=0");
            return;
        }

        LocalMultiControlLogger.Info($"本地回环消息分发: {messageType.Name}, sender={senderId}, handlers={handlers.Count}");
        foreach (Delegate handler in handlers)
        {
            if (handler is MessageHandlerDelegate<T> typedHandler)
            {
                typedHandler(message, senderId);
            }
        }
    }

    public void Update()
    {
    }

    public void Disconnect(NetError reason, bool now = false)
    {
        if (!IsConnected)
        {
            return;
        }

        IsConnected = false;
        LocalMultiControlLogger.Info($"本地回环网络断开: reason={reason}, now={now}");
        Disconnected?.Invoke(new NetErrorInfo(reason, selfInitiated: true));
    }

    public ConnectionStats? GetStatsForPeer(ulong peerId)
    {
        return null;
    }

    public void SetGameLoading(bool isLoading)
    {
        IsGameLoading = isLoading;
        LocalMultiControlLogger.Info($"本地回环加载状态更新: {isLoading}");
    }

    public string? GetRawLobbyIdentifier()
    {
        return "local-self-coop";
    }

    public void DisconnectClient(ulong peerId, NetError reason, bool now = false)
    {
        LocalMultiControlLogger.Warn($"本地回环请求断开客户端被忽略: peer={peerId}, reason={reason}, now={now}");
        ClientDisconnected?.Invoke(peerId, new NetErrorInfo(reason, selfInitiated: true));
    }

    public void SetPeerReadyForBroadcasting(ulong peerId)
    {
        LocalMultiControlLogger.Info($"本地回环设置广播就绪（占位）: peer={peerId}");
        ClientConnected?.Invoke(peerId);
    }

    private void TryDispatchSyntheticSecondPlayerSync<T>(T message) where T : INetMessage
    {
        if (_currentSenderId != LocalSelfCoopContext.PrimaryPlayerId)
        {
            return;
        }

        if (message is not SyncPlayerDataMessage)
        {
            return;
        }

        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            return;
        }

        var secondPlayer = runState.Players.FirstOrDefault((player) => player.NetId == LocalSelfCoopContext.SecondaryPlayerId);
        if (secondPlayer == null)
        {
            return;
        }

        SyncPlayerDataMessage syntheticMessage = new SyncPlayerDataMessage
        {
            player = secondPlayer.ToSerializable()
        };
        DispatchLoopback(syntheticMessage, LocalSelfCoopContext.SecondaryPlayerId);
        LocalMultiControlLogger.Info("本地回环已注入第二玩家同步消息，避免开局同步阻塞。");
    }
}
