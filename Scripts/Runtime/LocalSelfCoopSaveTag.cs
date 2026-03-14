using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Saves;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalSelfCoopSaveTag
{
    private const string SaveTagFileName = "local_self_coop_mp.tag";
    private const string V2Prefix = "v2:";

    public static void MarkCurrentProfile(ulong primaryPlayerId, ulong secondaryPlayerId)
    {
        MarkCurrentProfile(new List<ulong> { primaryPlayerId, secondaryPlayerId });
    }

    public static void MarkCurrentProfile(IReadOnlyList<ulong> playerIds)
    {
        try
        {
            List<ulong> normalized = playerIds
                .Where((id) => id != 0)
                .Distinct()
                .Take(4)
                .ToList();
            if (normalized.Count < 2)
            {
                LocalMultiControlLogger.Warn("写入本地多控存档标记失败：有效玩家ID不足2个。");
                return;
            }

            string serialized = $"{V2Prefix}{string.Join(",", normalized)}";
            GodotFileIo fileIo = new GodotFileIo(UserDataPathProvider.GetProfileScopedPath(SaveManager.Instance.CurrentProfileId, UserDataPathProvider.SavesDir));
            fileIo.WriteFile(SaveTagFileName, serialized);
            LocalMultiControlLogger.Info($"已写入本地多控存档标记: {serialized}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"写入本地多控存档标记失败: {exception.Message}");
        }
    }

    public static void ClearCurrentProfile()
    {
        try
        {
            GodotFileIo fileIo = new GodotFileIo(UserDataPathProvider.GetProfileScopedPath(SaveManager.Instance.CurrentProfileId, UserDataPathProvider.SavesDir));
            if (fileIo.FileExists(SaveTagFileName))
            {
                fileIo.DeleteFile(SaveTagFileName);
                LocalMultiControlLogger.Info("已清理本地多控存档标记。");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"清理本地多控存档标记失败: {exception.Message}");
        }
    }

    public static bool TryReadCurrentProfile(out ulong primaryPlayerId, out ulong secondaryPlayerId)
    {
        primaryPlayerId = 0;
        secondaryPlayerId = 0;
        if (!TryReadCurrentProfile(out List<ulong> playerIds) || playerIds.Count < 2)
        {
            return false;
        }

        primaryPlayerId = playerIds[0];
        secondaryPlayerId = playerIds[1];
        return true;
    }

    public static bool TryReadCurrentProfile(out List<ulong> playerIds)
    {
        playerIds = new List<ulong>();

        try
        {
            GodotFileIo fileIo = new GodotFileIo(UserDataPathProvider.GetProfileScopedPath(SaveManager.Instance.CurrentProfileId, UserDataPathProvider.SavesDir));
            if (!fileIo.FileExists(SaveTagFileName))
            {
                return false;
            }

            string? content = fileIo.ReadFile(SaveTagFileName);
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            if (content.StartsWith(V2Prefix, StringComparison.OrdinalIgnoreCase))
            {
                string payload = content.Substring(V2Prefix.Length);
                List<ulong> parsedV2 = ParseIds(payload);
                if (parsedV2.Count < 2)
                {
                    LocalMultiControlLogger.Warn($"本地多控存档标记格式无效: {content}");
                    return false;
                }

                playerIds = parsedV2;
                return true;
            }

            // 向后兼容旧双人格式: "id1,id2"
            List<ulong> parsedLegacy = ParseIds(content);
            if (parsedLegacy.Count == 2)
            {
                playerIds = parsedLegacy;
                return true;
            }

            LocalMultiControlLogger.Warn($"本地多控存档标记格式无效: {content}");
            return false;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"读取本地多控存档标记失败: {exception.Message}");
            return false;
        }
    }

    private static List<ulong> ParseIds(string payload)
    {
        return payload
            .Split(',')
            .Select((part) => part.Trim())
            .Where((part) => ulong.TryParse(part, out _))
            .Select(ulong.Parse)
            .Where((id) => id != 0)
            .Distinct()
            .Take(4)
            .ToList();
    }
}
