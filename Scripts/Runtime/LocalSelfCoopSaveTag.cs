using System;
using System.Linq;
using MegaCrit.Sts2.Core.Saves;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalSelfCoopSaveTag
{
    private const string SaveTagFileName = "local_self_coop_mp.tag";

    public static void MarkCurrentProfile(ulong primaryPlayerId, ulong secondaryPlayerId)
    {
        try
        {
            GodotFileIo fileIo = new GodotFileIo(UserDataPathProvider.GetProfileScopedPath(SaveManager.Instance.CurrentProfileId, UserDataPathProvider.SavesDir));
            fileIo.WriteFile(SaveTagFileName, $"{primaryPlayerId},{secondaryPlayerId}");
            LocalMultiControlLogger.Info($"已写入本地双人存档标记: primary={primaryPlayerId}, secondary={secondaryPlayerId}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"写入本地双人存档标记失败: {exception.Message}");
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
                LocalMultiControlLogger.Info("已清理本地双人存档标记。");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"清理本地双人存档标记失败: {exception.Message}");
        }
    }

    public static bool TryReadCurrentProfile(out ulong primaryPlayerId, out ulong secondaryPlayerId)
    {
        primaryPlayerId = 0;
        secondaryPlayerId = 0;

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

            string[] parts = content.Split(',').Select((part) => part.Trim()).ToArray();
            if (parts.Length != 2 || !ulong.TryParse(parts[0], out primaryPlayerId) || !ulong.TryParse(parts[1], out secondaryPlayerId))
            {
                LocalMultiControlLogger.Warn($"本地双人存档标记格式无效: {content}");
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"读取本地双人存档标记失败: {exception.Message}");
            return false;
        }
    }
}
