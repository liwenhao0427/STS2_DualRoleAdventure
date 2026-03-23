using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rewards;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RewardsCmd), nameof(RewardsCmd.OfferCustom))]
internal static class RewardsCmdOfferCustomPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player player, ref List<Reward> rewards)
    {
        if (!CrystalSphereMirrorRuntime.IsInCrystalSphereEventContext(player) || rewards.Count == 0)
        {
            return;
        }

        // 已存在其他玩家的卡牌奖励时，认为本轮已扩展，避免重复插入。
        if (rewards.OfType<CardReward>().Any((reward) => reward.Player.NetId != player.NetId))
        {
            return;
        }

        List<Player> otherPlayers = CrystalSphereMirrorRuntime.GetOtherPlayers(player);
        if (otherPlayers.Count == 0)
        {
            return;
        }

        List<Reward> sourceRewards = rewards
            .Where((reward) => reward is CardReward cardReward && cardReward.Player.NetId == player.NetId)
            .ToList();
        if (sourceRewards.Count == 0)
        {
            return;
        }

        List<Reward> expandedRewards = new(rewards);
        int extraCardRewardCount = 0;
        foreach (Reward sourceReward in sourceRewards)
        {
            if (sourceReward is not CardReward sourceCardReward)
            {
                continue;
            }

            CardCreationOptions? options = AccessTools.Property(typeof(CardReward), "Options")
                ?.GetValue(sourceCardReward) as CardCreationOptions;
            int optionCount = AccessTools.Property(typeof(CardReward), "OptionCount")
                ?.GetValue(sourceCardReward) as int? ?? 3;
            if (options is null)
            {
                continue;
            }

            int insertIndex = expandedRewards.IndexOf(sourceReward);
            int insertOffset = 1;
            foreach (Player otherPlayer in otherPlayers)
            {
                CardCreationOptions mirroredOptions = BuildMirroredOptions(options, otherPlayer);
                CardReward mirroredReward = new(mirroredOptions, optionCount, otherPlayer)
                {
                    CanReroll = sourceCardReward.CanReroll
                };

                if (insertIndex >= 0)
                {
                    expandedRewards.Insert(insertIndex + insertOffset, mirroredReward);
                }
                else
                {
                    expandedRewards.Add(mirroredReward);
                }

                insertOffset++;
                extraCardRewardCount++;
            }
        }

        rewards = expandedRewards;
        LocalMultiControlLogger.Info(
            $"水晶球 OfferCustom 卡牌奖励已按本地多控扩展: owner={player.NetId}, extraPlayers={otherPlayers.Count}, extraRewards={extraCardRewardCount}");
    }

    private static CardCreationOptions BuildMirroredOptions(CardCreationOptions sourceOptions, Player targetPlayer)
    {
        CardCreationOptions mirroredOptions;
        if (sourceOptions.CustomCardPool != null)
        {
            mirroredOptions = new CardCreationOptions(sourceOptions.CustomCardPool, sourceOptions.Source, sourceOptions.RarityOdds);
        }
        else
        {
            mirroredOptions = new CardCreationOptions(
                new[] { targetPlayer.Character.CardPool },
                sourceOptions.Source,
                sourceOptions.RarityOdds,
                sourceOptions.CardPoolFilter);
        }

        if (sourceOptions.Flags != 0)
        {
            mirroredOptions.WithFlags(sourceOptions.Flags);
        }

        if (sourceOptions.RngOverride != null)
        {
            mirroredOptions.WithRngOverride(sourceOptions.RngOverride);
        }

        return mirroredOptions;
    }
}
