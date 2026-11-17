using System.Diagnostics;
using System.IO;

namespace Otc.Updater;

/// <summary>更新进度。</summary>
public sealed class UpdateProgress
{
    /// <summary>百分比。</summary>
    public double Percent { get; init; }

    /// <summary>当前状态。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>日志行。</summary>
    public string Log { get; init; } = string.Empty;

    /// <summary>是否错误状态。</summary>
    public bool IsError { get; init; }
}

/// <summary>执行通用更新流程。</summary>
public static class UpdateRunner
{
    /// <summary>执行更新。</summary>
    public static async Task<UpdateResult> RunAsync(
        UpdateLaunchOptions options,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var updateRoot = string.IsNullOrWhiteSpace(options.UpdateRoot)
            ? UpdateRuntimeFiles.DefaultRoot
            : options.UpdateRoot;
        var logDirectory = Path.Combine(updateRoot, "logs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"{options.ProductCode}-updater.log");

        void Report(double percent, string status, string log = "", bool isError = false)
        {
            progress?.Report(new UpdateProgress
            {
                Percent = Math.Clamp(percent, 0, 100),
                Status = status,
                Log = log,
                IsError = isError,
            });

            if (!string.IsNullOrWhiteSpace(log))
            {
                File.AppendAllText(
                    logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {log}{Environment.NewLine}");
            }
        }

        string? backupPath = null;
        Process? newProcess = null;

        try
        {
            ValidateOptions(options);
            Report(5, "等待主程序退出", "Updater started");
            await WaitForParentExitAsync(options.ParentProcessId, cancellationToken);

            Report(20, "验证更新包", "Validating package");
            UpdatePackageService.ValidateApprovedPackage(
                options.PackagePath,
                options.ProductCode,
                options.ProductType,
                options.ToVersion,
                options.PackageSha256,
                options.PackageSize,
                options.ManifestSignature,
                options.PublicKeyPem);

            var workDirectory = Path.Combine(
                updateRoot,
                "work",
                options.ProductCode,
                $"{options.ToVersion}-{Guid.NewGuid():N}");
            Report(35, "解包并校验文件", "Extracting payload");
            UpdatePackageService.ExtractPayload(options.PackagePath, workDirectory);

            Report(50, "备份当前版本", "Creating backup");
            backupPath = UpdateFileSystem.CreateBackup(
                options.InstallDirectory,
                options.BackupRoot,
                options.ProductCode,
                options.FromVersion);

            Report(65, "替换程序文件", "Applying payload");
            UpdateFileSystem.ApplyPayload(workDirectory, options.InstallDirectory);

            var healthPath = UpdateRuntimeFiles.GetHealthFilePath(
                updateRoot,
                options.ProductCode);
            if (File.Exists(healthPath))
            {
                File.Delete(healthPath);
            }

            Report(75, "启动新版本", "Starting new application");
            newProcess = StartApplication(options);

            Report(85, "执行健康检查", "Waiting for health marker");
            if (!await WaitForHealthyVersionAsync(
                    newProcess,
                    updateRoot,
                    options.ProductCode,
                    options.ToVersion,
                    options.HealthTimeoutSeconds,
                    cancellationToken))
            {
                throw new InvalidOperationException("新版本未产生有效健康标记");
            }

            var result = new UpdateResult
            {
                State = "success",
                ProductCode = options.ProductCode,
                FromVersion = options.FromVersion,
                ToVersion = options.ToVersion,
                Message = "新版本已启动并通过健康检查",
                BackupPath = backupPath,
                FinishedAtUtc = DateTime.UtcNow,
            };
            UpdateRuntimeFiles.WriteResult(updateRoot, result);
            UpdateFileSystem.CleanupOldBackups(options.BackupRoot, 3);
            Report(100, "更新成功", "New version health check passed");
            return result;
        }
        catch (Exception ex)
        {
            Report(90, "更新失败，准备回滚", ex.Message, true);

            try
            {
                if (newProcess is { HasExited: false })
                {
                    newProcess.Kill(entireProcessTree: true);
                    newProcess.WaitForExit(10000);
                }

                if (!string.IsNullOrWhiteSpace(backupPath))
                {
                    UpdateFileSystem.RestoreBackup(backupPath, options.InstallDirectory);
                    StartApplication(options);
                }

                var result = new UpdateResult
                {
                    State = string.IsNullOrWhiteSpace(backupPath) ? "failed" : "rolled_back",
                    ProductCode = options.ProductCode,
                    FromVersion = options.FromVersion,
                    ToVersion = options.ToVersion,
                    Message = ex.Message,
                    BackupPath = backupPath ?? string.Empty,
                    FinishedAtUtc = DateTime.UtcNow,
                };
                UpdateRuntimeFiles.WriteResult(updateRoot, result);
                Report(100, result.Succeeded ? "更新成功" : "已自动回滚", ex.Message, true);
                return result;
            }
            catch (Exception rollbackError)
            {
                var result = new UpdateResult
                {
                    State = "failed",
                    ProductCode = options.ProductCode,
                    FromVersion = options.FromVersion,
                    ToVersion = options.ToVersion,
                    Message = $"更新失败：{ex.Message}；回滚失败：{rollbackError.Message}",
                    BackupPath = backupPath ?? string.Empty,
                    FinishedAtUtc = DateTime.UtcNow,
                };
                UpdateRuntimeFiles.WriteResult(updateRoot, result);
                Report(100, "更新和回滚均失败", result.Message, true);
                return result;
            }
        }
    }

    private static void ValidateOptions(UpdateLaunchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProductCode)
            || string.IsNullOrWhiteSpace(options.ProductType)
            || string.IsNullOrWhiteSpace(options.ToVersion)
            || string.IsNullOrWhiteSpace(options.PackagePath)
            || string.IsNullOrWhiteSpace(options.InstallDirectory)
            || string.IsNullOrWhiteSpace(options.ApplicationExecutable)
            || string.IsNullOrWhiteSpace(options.PublicKeyPem)
            || string.IsNullOrWhiteSpace(options.BackupRoot))
        {
            throw new ArgumentException("更新器启动参数不完整");
        }

        if (!File.Exists(options.PackagePath))
        {
            throw new FileNotFoundException("更新包不存在", options.PackagePath);
        }

        if (!File.Exists(Path.Combine(options.InstallDirectory, options.ApplicationExecutable)))
        {
            throw new FileNotFoundException("主程序不存在", options.ApplicationExecutable);
        }
    }

    private static Process StartApplication(UpdateLaunchOptions options)
    {
        var executable = Path.Combine(options.InstallDirectory, options.ApplicationExecutable);
        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = options.InstallDirectory,
            UseShellExecute = true,
        }) ?? throw new InvalidOperationException("无法启动主程序");
    }

    private static async Task WaitForParentExitAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        if (processId <= 0)
        {
            return;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            var timeout = DateTime.UtcNow.AddSeconds(60);
            while (!process.HasExited && DateTime.UtcNow < timeout)
            {
                await Task.Delay(200, cancellationToken);
            }

            if (!process.HasExited)
            {
                throw new TimeoutException("主程序未在 60 秒内退出");
            }
        }

        await Task.Delay(800, cancellationToken);
    }

    private static async Task<bool> WaitForHealthyVersionAsync(
        Process process,
        string updateRoot,
        string productCode,
        string expectedVersion,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var startedAt = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                return false;
            }

            var marker = UpdateRuntimeFiles.ReadHealthMarker(updateRoot, productCode);
            if (marker is not null
                && marker.ProcessId == process.Id
                && marker.Version.Equals(expectedVersion, StringComparison.OrdinalIgnoreCase)
                && marker.StartedAtUtc >= startedAt.AddSeconds(-2))
            {
                return true;
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }
}
