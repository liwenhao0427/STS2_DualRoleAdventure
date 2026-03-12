using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using Godot;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalMultiControlRuntime
{
    private static readonly LocalMultiSessionState Session = new LocalMultiSessionState();

    private static readonly HashSet<string> _fieldSyncFailures = new HashSet<string>();

    public static LocalMultiSessionState SessionState => Session;

    public static void OnRunLaunched(RunState runState)
    {
        LocalMultiControlLogger.Info("检测到 RunManager.Launch，开始初始化本地多控会话。");
        if (LocalSelfCoopContext.IsEnabled)
        {
            RunManager.Instance.CombatStateSynchronizer.IsDisabled = true;
            LocalMultiControlLogger.Info("本地双人模式已禁用战斗同步等待，避免单进程回环阻塞。");
        }

        Session.InitializeFromRunState(runState);
        if (Session.CurrentControlledPlayerId.HasValue)
        {
            ApplyControlContext("run-launched");
        }
        else
        {
            LocalMultiControlLogger.Info("当前运行未启用本地多控会话。");
        }
    }

    public static void OnRunCleanup()
    {
        Session.Reset("RunManager.CleanUp");
        LocalSelfCoopContext.Disable("RunManager.CleanUp");
        LocalMultiControlLogger.Info("RunManager.CleanUp 后已完成本地多控会话清理。");
    }

    public static void SwitchNextControlledPlayer(string source)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        if (Session.SwitchNextPlayer())
        {
            ApplyControlContext(source);
        }
    }

    public static void SwitchPreviousControlledPlayer(string source)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        if (Session.SwitchPreviousPlayer())
        {
            ApplyControlContext(source);
        }
    }

    private static void ApplyControlContext(string source)
    {
        ulong? currentControlledPlayerId = Session.CurrentControlledPlayerId;
        if (!currentControlledPlayerId.HasValue)
        {
            return;
        }

        ulong? previousNetId = LocalContext.NetId;
        LocalContext.NetId = currentControlledPlayerId.Value;
        LocalSelfCoopContext.NetService?.SetCurrentSenderId(currentControlledPlayerId.Value);
        SyncRunSynchronizerLocalPlayerId(currentControlledPlayerId.Value);
        RefreshCombatUiForControlledPlayer(currentControlledPlayerId.Value);
        RefreshTopBarForControlledPlayer(currentControlledPlayerId.Value);
        RefreshEventRoomForControlledPlayer(currentControlledPlayerId.Value);
        LocalMultiControlLogger.Info($"控制上下文已更新: {previousNetId?.ToString() ?? "null"} -> {currentControlledPlayerId.Value}, source={source}");
        if (source != "run-launched")
        {
            string slotLabel = currentControlledPlayerId.Value == LocalSelfCoopContext.PrimaryPlayerId ? "1" : "2";
            NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create($"控制角色: 槽位{slotLabel}"));
        }
    }

    private static void SyncRunSynchronizerLocalPlayerId(ulong playerId)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        TrySetLocalPlayerId(RunManager.Instance.EventSynchronizer, playerId, nameof(RunManager.EventSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.RewardSynchronizer, playerId, nameof(RunManager.RewardSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.RestSiteSynchronizer, playerId, nameof(RunManager.RestSiteSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.OneOffSynchronizer, playerId, nameof(RunManager.OneOffSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.TreasureRoomRelicSynchronizer, playerId, nameof(RunManager.TreasureRoomRelicSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.FlavorSynchronizer, playerId, nameof(RunManager.FlavorSynchronizer));
    }

    private static void TrySetLocalPlayerId(object? target, ulong playerId, string componentName)
    {
        if (target == null)
        {
            return;
        }

        try
        {
            AccessTools.Field(target.GetType(), "_localPlayerId")?.SetValue(target, playerId);
        }
        catch (Exception exception)
        {
            string key = $"{componentName}:{target.GetType().Name}";
            if (_fieldSyncFailures.Add(key))
            {
                LocalMultiControlLogger.Warn($"同步 {key} 的 _localPlayerId 失败: {exception.Message}");
            }
        }
    }

    public static void TryAutoSwitchAfterEndTurn(ulong endedPlayerId)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        if (Session.CurrentControlledPlayerId != endedPlayerId)
        {
            return;
        }

        if (CombatManager.Instance.AllPlayersReadyToEndTurn())
        {
            LocalMultiControlLogger.Info("所有角色均已结束回合，跳过自动切换，等待敌方回合推进。");
            return;
        }

        LocalMultiControlLogger.Info($"检测到角色 {endedPlayerId} 结束回合，自动切换到下一位。");
        Callable.From(delegate
        {
            SwitchNextControlledPlayer("auto-end-turn");
        }).CallDeferred();
    }

    private static void RefreshCombatUiForControlledPlayer(ulong playerId)
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            return;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return;
        }

        CombatState? combatState = AccessTools.Field(typeof(NEndTurnButton), "_combatState")?.GetValue(combatUi.EndTurnButton) as CombatState;
        if (combatState == null)
        {
            return;
        }

        Player? player = combatState.GetPlayer(playerId);
        if (player == null)
        {
            LocalMultiControlLogger.Warn($"刷新战斗UI失败：未找到玩家 {playerId}");
            return;
        }

        try
        {
            CardPile handPile = PileType.Hand.GetPile(player);
            AccessTools.Field(typeof(NEndTurnButton), "_playerHand")?.SetValue(combatUi.EndTurnButton, handPile);
            combatUi.DrawPile.Initialize(player);
            combatUi.DiscardPile.Initialize(player);
            combatUi.ExhaustPile.Initialize(player);

            NPlayerHand hand = combatUi.Hand;
            hand.CancelAllCardPlay();
            foreach (NCardHolder holder in hand.CardHolderContainer.GetChildren().OfType<NCardHolder>().ToList())
            {
                hand.RemoveCardHolder(holder);
            }

            foreach (CardModel card in handPile.Cards)
            {
                NCard? cardNode = NCard.Create(card);
                if (cardNode != null)
                {
                    hand.Add(cardNode);
                }
            }

            hand.ForceRefreshCardIndices();
            RefreshCombatEnergyUi(combatUi, player);
            ReevaluateEndTurnButtonState(combatUi, combatState);
            LocalMultiControlLogger.Info($"战斗UI已刷新到当前角色 {playerId}，手牌数量={handPile.Cards.Count}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"刷新战斗UI失败: {exception.Message}");
        }
    }

    private static void RefreshCombatEnergyUi(NCombatUi combatUi, Player player)
    {
        NStarCounter? starCounter = AccessTools.Field(typeof(NCombatUi), "_starCounter")?.GetValue(combatUi) as NStarCounter;
        NEnergyCounter? oldEnergyCounter = AccessTools.Field(typeof(NCombatUi), "_energyCounter")?.GetValue(combatUi) as NEnergyCounter;

        if (starCounter != null)
        {
            Player? previousPlayer = AccessTools.Field(typeof(NStarCounter), "_player")?.GetValue(starCounter) as Player;
            if (previousPlayer != null)
            {
                MethodInfo? onStarsChangedMethod = AccessTools.Method(typeof(NStarCounter), "OnStarsChanged");
                if (onStarsChangedMethod != null)
                {
                    Action<int, int> onStarsChanged = (Action<int, int>)onStarsChangedMethod.CreateDelegate(typeof(Action<int, int>), starCounter);
                    if (previousPlayer.PlayerCombatState != null)
                    {
                        previousPlayer.PlayerCombatState.StarsChanged -= onStarsChanged;
                    }
                }
            }

            starCounter.Initialize(player);
        }

        if (oldEnergyCounter != null)
        {
            oldEnergyCounter.QueueFreeSafely();
        }

        NEnergyCounter? newEnergyCounter = NEnergyCounter.Create(player);
        if (newEnergyCounter != null)
        {
            combatUi.EnergyCounterContainer.AddChildSafely(newEnergyCounter);
            starCounter?.Reparent(newEnergyCounter);
            AccessTools.Field(typeof(NCombatUi), "_energyCounter")?.SetValue(combatUi, newEnergyCounter);
        }
    }

    private static void RefreshTopBarForControlledPlayer(ulong playerId)
    {
        NTopBar? topBar = NRun.Instance?.GlobalUi?.TopBar;
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (topBar == null || runState == null)
        {
            return;
        }

        Player? player = runState.GetPlayer(playerId);
        if (player == null)
        {
            return;
        }

        try
        {
            RefreshTopBarDeck(topBar.Deck, player);
            topBar.Gold.Initialize(player);
            topBar.Hp.Initialize(player);
            foreach (Node child in topBar.Portrait.GetChildren())
            {
                child.QueueFreeSafely();
            }

            topBar.Portrait.Initialize(player);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"刷新顶部栏失败: {exception.Message}");
        }
    }

    private static void RefreshTopBarDeck(NTopBarDeckButton deckButton, Player player)
    {
        CardPile? oldPile = AccessTools.Field(typeof(NTopBarDeckButton), "_pile")?.GetValue(deckButton) as CardPile;
        MethodInfo? updateMethod = AccessTools.Method(typeof(NTopBarDeckButton), "OnPileContentsChanged");
        if (oldPile != null && updateMethod != null)
        {
            Action updateHandler = (Action)Delegate.CreateDelegate(typeof(Action), deckButton, updateMethod);
            oldPile.CardAddFinished -= updateHandler;
            oldPile.CardRemoveFinished -= updateHandler;
        }

        deckButton.Initialize(player);
    }

    private static void RefreshEventRoomForControlledPlayer(ulong playerId)
    {
        NEventRoom? eventRoom = NEventRoom.Instance;
        EventSynchronizer synchronizer = RunManager.Instance.EventSynchronizer;
        if (eventRoom == null || synchronizer.IsShared)
        {
            return;
        }

        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        Player? player = runState?.GetPlayer(playerId);
        if (player == null)
        {
            return;
        }

        EventModel targetEvent = synchronizer.GetEventForPlayer(player);
        EventModel? currentEvent = AccessTools.Field(typeof(NEventRoom), "_event")?.GetValue(eventRoom) as EventModel;
        if (currentEvent == null || currentEvent == targetEvent)
        {
            return;
        }

        try
        {
            Action<EventModel> refreshHandler = (Action<EventModel>)AccessTools.Method(typeof(NEventRoom), "RefreshEventState")!
                .CreateDelegate(typeof(Action<EventModel>), eventRoom);
            Action enteringCombatHandler = (Action)AccessTools.Method(typeof(NEventRoom), "OnEnteringEventCombat")!
                .CreateDelegate(typeof(Action), eventRoom);
            Func<EventOption, Task> beforeChosenHandler = (Func<EventOption, Task>)AccessTools.Method(typeof(NEventRoom), "BeforeOptionChosen")!
                .CreateDelegate(typeof(Func<EventOption, Task>), eventRoom);

            currentEvent.StateChanged -= refreshHandler;
            currentEvent.EnteringEventCombat -= enteringCombatHandler;

            List<EventOption>? connectedOptions = AccessTools.Field(typeof(NEventRoom), "_connectedOptions")?.GetValue(eventRoom) as List<EventOption>;
            if (connectedOptions != null)
            {
                foreach (EventOption option in connectedOptions)
                {
                    option.BeforeChosen -= beforeChosenHandler;
                }

                connectedOptions.Clear();
            }

            AccessTools.Field(typeof(NEventRoom), "_event")?.SetValue(eventRoom, targetEvent);
            targetEvent.StateChanged += refreshHandler;
            targetEvent.EnteringEventCombat += enteringCombatHandler;

            AccessTools.Method(typeof(NEventRoom), "SetTitle")?.Invoke(eventRoom, new object[] { targetEvent.Title });
            AccessTools.Method(typeof(NEventRoom), "RefreshEventState")?.Invoke(eventRoom, new object[] { targetEvent });
            LocalMultiControlLogger.Info($"非共享事件视图已切换到玩家 {playerId}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"切换非共享事件视图失败: {exception.Message}");
        }
    }

    private static void ReevaluateEndTurnButtonState(NCombatUi combatUi, CombatState combatState)
    {
        if (combatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        try
        {
            Player? currentPlayer = combatState.GetPlayer(LocalContext.NetId ?? 0);
            if (currentPlayer != null)
            {
                bool shouldDisable = CombatManager.Instance.IsPlayerReadyToEndTurn(currentPlayer);
                AccessTools.PropertySetter(typeof(CombatManager), "PlayerActionsDisabled")?.Invoke(CombatManager.Instance, new object[] { shouldDisable });
            }

            AccessTools.Method(typeof(NEndTurnButton), "OnTurnStarted")?.Invoke(combatUi.EndTurnButton, new object[] { combatState });
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"刷新回合结束按钮状态失败: {exception.Message}");
        }
    }
}
