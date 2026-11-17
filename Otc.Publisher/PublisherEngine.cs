using Npgsql;
using System.IO;

namespace Otc.Publisher;

/// <summary>通用更新包发布引擎。</summary>
public static class PublisherEngine
{
    /// <summary>生成 RSA 密钥对。</summary>
    public static void GenerateKeys(string privateKeyPath, string publicKeyPath)
    {
        var (privateKey, publicKey) = UpdateCryptography.GenerateRsaKeyPair();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(privateKeyPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(publicKeyPath))!);
        File.WriteAllText(privateKeyPath, privateKey);
        File.WriteAllText(publicKeyPath, publicKey);
    }

    /// <summary>发布更新包并写入数据库。</summary>
    public static async Task<PublisherResult> PublishAsync(
        PublisherOptions options,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        Validate(options);
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "OTC.UpdatePublisher",
            Guid.NewGuid().ToString("N"));
        var filteredSource = Path.Combine(temporaryRoot, "source");
        var packagePath = Path.Combine(
            temporaryRoot,
            $"{options.ProductCode}_{options.Version}{UpdateConstants.PackageExtension}");

        try
        {
            log?.Invoke("整理发布文件");
            CopyPublishedFiles(options.SourceDirectory, filteredSource);

            if (!string.IsNullOrWhiteSpace(options.UpdaterDirectory))
            {
                log?.Invoke("加入通用 Updater");
                CopyDirectory(options.UpdaterDirectory, Path.Combine(filteredSource, "Updater"));
            }

            log?.Invoke("生成并签名更新包");
            var manifest = UpdatePackageService.BuildPackage(
                filteredSource,
                options.ProductCode,
                options.ProductType,
                options.Version,
                packagePath);
            var signature = UpdateCryptography.SignManifest(
                manifest,
                File.ReadAllText(options.PrivateKeyPath));
            var sha256 = UpdateCryptography.ComputeSha256(packagePath);
            var fileSize = new FileInfo(packagePath).Length;

            var relativePath = Path.Combine(
                options.ProductCode,
                options.Version,
                Path.GetFileName(packagePath));
            var remotePath = Path.Combine(options.ShareDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(remotePath)!);
            File.Copy(packagePath, remotePath, true);
            log?.Invoke($"更新包已复制到 {remotePath}");

            log?.Invoke("写入数据库发布记录");
            await WriteDatabaseAsync(
                options,
                relativePath,
                sha256,
                fileSize,
                signature,
                cancellationToken);

            return new PublisherResult
            {
                PackagePath = remotePath,
                Sha256 = sha256,
                FileSize = fileSize,
            };
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, true);
            }
        }
    }

    private static async Task WriteDatabaseAsync(
        PublisherOptions options,
        string relativePath,
        string sha256,
        long fileSize,
        string signature,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var delete = new NpgsqlCommand(
                         """
                         DELETE FROM n_software_version
                         WHERE product_code = @product_code
                           AND version = @version
                         """,
                         connection,
                         transaction))
        {
            delete.Parameters.AddWithValue("product_code", options.ProductCode);
            delete.Parameters.AddWithValue("version", options.Version);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO n_software_version
                         (
                             version, type, product_code, file_path, sha256, file_size,
                             manifest_signature, mandatory, target_type, target_value,
                             rollout_percent, remark, create_at
                         )
                         VALUES
                         (
                             @version, @type, @product_code, @file_path, @sha256, @file_size,
                             @manifest_signature, @mandatory, @target_type, @target_value,
                             @rollout_percent, @remark, NOW()
                         )
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue("version", options.Version);
            insert.Parameters.AddWithValue("type", options.ProductType);
            insert.Parameters.AddWithValue("product_code", options.ProductCode);
            insert.Parameters.AddWithValue("file_path", relativePath);
            insert.Parameters.AddWithValue("sha256", sha256);
            insert.Parameters.AddWithValue("file_size", fileSize);
            insert.Parameters.AddWithValue("manifest_signature", signature);
            insert.Parameters.AddWithValue("mandatory", options.Mandatory);
            insert.Parameters.AddWithValue("target_type", options.TargetType);
            insert.Parameters.AddWithValue("target_value", options.TargetValue);
            insert.Parameters.AddWithValue("rollout_percent", options.RolloutPercent);
            insert.Parameters.AddWithValue("remark", options.Remark);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static void Validate(PublisherOptions options)
    {
        if (!Directory.Exists(options.SourceDirectory))
        {
            throw new DirectoryNotFoundException(options.SourceDirectory);
        }

        if (!string.IsNullOrWhiteSpace(options.UpdaterDirectory)
            && !Directory.Exists(options.UpdaterDirectory))
        {
            throw new DirectoryNotFoundException(options.UpdaterDirectory);
        }

        if (!File.Exists(options.PrivateKeyPath))
        {
            throw new FileNotFoundException("签名私钥不存在", options.PrivateKeyPath);
        }

        if (string.IsNullOrWhiteSpace(options.ProductCode)
            || string.IsNullOrWhiteSpace(options.ProductType)
            || string.IsNullOrWhiteSpace(options.Version)
            || string.IsNullOrWhiteSpace(options.ShareDirectory)
            || string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("发布参数不完整");
        }
    }

    private static void CopyPublishedFiles(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        var sourceRoot = Path.GetFullPath(source);

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            if (ShouldExclude(relative))
            {
                continue;
            }

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static bool ShouldExclude(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.Equals("appsettings.Local.json", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("logs/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("UserAvatars/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Updates/", StringComparison.OrdinalIgnoreCase);
    }
}
