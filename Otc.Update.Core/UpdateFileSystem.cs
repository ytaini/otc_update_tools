using System.IO;
using System.Linq;

namespace Otc.Update.Core;

/// <summary>更新器使用的目录备份、替换与恢复操作。</summary>
public static class UpdateFileSystem
{
    /// <summary>创建完整安装目录备份。</summary>
    public static string CreateBackup(
        string installDirectory,
        string backupRoot,
        string productCode,
        string fromVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);

        var safeVersion = string.IsNullOrWhiteSpace(fromVersion) ? "unknown" : fromVersion;
        var backupPath = Path.Combine(
            backupRoot,
            productCode,
            $"{safeVersion}-{DateTime.Now:yyyyMMdd-HHmmss}");

        CopyDirectory(installDirectory, backupPath);
        return backupPath;
    }

    /// <summary>把暂存负载覆盖复制到安装目录。</summary>
    public static void ApplyPayload(string payloadDirectory, string installDirectory)
    {
        CopyDirectory(payloadDirectory, installDirectory);
    }

    /// <summary>更新失败时用备份恢复安装目录。</summary>
    public static void RestoreBackup(string backupDirectory, string installDirectory)
    {
        CopyDirectory(backupDirectory, installDirectory);
    }

    /// <summary>仅保留最近若干个备份目录。</summary>
    public static void CleanupOldBackups(string backupRoot, int keepCount)
    {
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        foreach (var productDirectory in Directory.EnumerateDirectories(backupRoot))
        {
            foreach (var backup in Directory.EnumerateDirectories(productDirectory)
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                         .Skip(Math.Max(keepCount, 1)))
            {
                Directory.Delete(backup, true);
            }
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var destinationRoot = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (sourceRoot.Equals(destinationRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(destinationRoot);

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(
                destinationRoot,
                Path.GetRelativePath(sourceRoot, directory)));
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, sourceFile);
            var destinationFile = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, true);
        }
    }
}
