using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Otc.Update.Core;

/// <summary>更新包校验失败。</summary>
public sealed class UpdateValidationException : Exception
{
    /// <summary>初始化校验异常。</summary>
    public UpdateValidationException(string message) : base(message)
    {
    }
}

/// <summary>更新包创建、读取、校验与安全解包。</summary>
public static class UpdatePackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// 将发布目录构建为更新包。
    /// </summary>
    public static UpdatePackageManifest BuildPackage(
        string sourceDirectory,
        string productCode,
        string productType,
        string version,
        string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(productType);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(sourceDirectory);
        }

        var manifest = new UpdatePackageManifest
        {
            Product = productCode,
            ProductType = productType,
            Version = version,
            PublishedAtUtc = DateTime.UtcNow,
        };

        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                FullPath = path,
                RelativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
            })
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();

        manifest.Files = files.Select(file => new UpdatePackageFile
        {
            RelativePath = file.RelativePath,
            Size = new FileInfo(file.FullPath).Length,
            Sha256 = UpdateCryptography.ComputeSha256(file.FullPath),
        }).ToList();
        manifest.FileCount = manifest.Files.Count;
        manifest.TotalSize = manifest.Files.Sum(file => file.Size);

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var temporaryPath = outputPath + ".building";

        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry(UpdateConstants.ManifestFileName, CompressionLevel.Optimal);

            using (var manifestStream = manifestEntry.Open())
            {
                JsonSerializer.Serialize(manifestStream, manifest, JsonOptions);
            }

            foreach (var file in files)
            {
                var entry = archive.CreateEntry(
                    $"{UpdateConstants.PayloadDirectoryName}/{file.RelativePath}",
                    CompressionLevel.Optimal);

                using var input = File.OpenRead(file.FullPath);
                using var output = entry.Open();
                input.CopyTo(output);
            }
        }

        File.Move(temporaryPath, outputPath, true);
        return manifest;
    }

    /// <summary>从更新包读取清单。</summary>
    public static UpdatePackageManifest ReadManifest(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.GetEntry(UpdateConstants.ManifestFileName)
            ?? throw new UpdateValidationException("更新包缺少 manifest.json");

        using var stream = entry.Open();
        return JsonSerializer.Deserialize<UpdatePackageManifest>(stream, JsonOptions)
            ?? throw new UpdateValidationException("更新包清单为空");
    }

    /// <summary>
    /// 校验完整包摘要、大小、清单产品、类型、版本和清单签名。
    /// </summary>
    public static UpdatePackageManifest ValidateApprovedPackage(
        string packagePath,
        string productCode,
        string productType,
        string version,
        string expectedSha256,
        long expectedSize,
        string manifestSignature,
        string publicKeyPem)
    {
        if (!File.Exists(packagePath))
        {
            throw new UpdateValidationException("更新包不存在");
        }

        var actualSize = new FileInfo(packagePath).Length;

        if (expectedSize <= 0 || actualSize != expectedSize)
        {
            throw new UpdateValidationException($"更新包大小不匹配：期望 {expectedSize}，实际 {actualSize}");
        }

        var actualSha256 = UpdateCryptography.ComputeSha256(packagePath);

        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateValidationException("更新包 SHA-256 不匹配");
        }

        var manifest = ReadManifest(packagePath);

        if (!manifest.Product.Equals(productCode, StringComparison.OrdinalIgnoreCase)
            || !manifest.ProductType.Equals(productType, StringComparison.OrdinalIgnoreCase)
            || !manifest.Version.Equals(version, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateValidationException("更新包清单的产品、类型或版本与数据库记录不匹配");
        }

        if (!UpdateCryptography.VerifyManifestSignature(manifest, manifestSignature, publicKeyPem))
        {
            throw new UpdateValidationException("更新包清单数字签名无效");
        }

        return manifest;
    }

    /// <summary>
    /// 安全解包到本机目录，并逐文件复核清单中的大小与 SHA-256。
    /// </summary>
    public static void ExtractPayload(string packagePath, string destinationDirectory)
    {
        var manifest = ReadManifest(packagePath);
        var destinationRoot = Path.GetFullPath(destinationDirectory);

        if (Directory.Exists(destinationRoot))
        {
            Directory.Delete(destinationRoot, true);
        }

        Directory.CreateDirectory(destinationRoot);

        using var archive = ZipFile.OpenRead(packagePath);
        var expectedFiles = manifest.Files.ToDictionary(
            file => file.RelativePath.Replace('\\', '/'),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            var prefix = UpdateConstants.PayloadDirectoryName + "/";

            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = entry.FullName[prefix.Length..].Replace('/', '\\');

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            if (!expectedFiles.TryGetValue(relativePath.Replace('\\', '/'), out var expected))
            {
                throw new UpdateValidationException($"更新包包含未登记文件：{relativePath}");
            }

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));

            if (!targetPath.StartsWith(destinationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateValidationException($"更新包包含非法路径：{relativePath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            using (var input = entry.Open())
            using (var output = File.Create(targetPath))
            {
                input.CopyTo(output);
            }

            var actualSize = new FileInfo(targetPath).Length;
            var actualHash = UpdateCryptography.ComputeSha256(targetPath);

            if (actualSize != expected.Size
                || !actualHash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateValidationException($"更新包文件校验失败：{relativePath}");
            }
        }

        var extracted = Directory.EnumerateFiles(destinationRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(destinationRoot, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!expectedFiles.Keys.All(extracted.Contains))
        {
            throw new UpdateValidationException("更新包缺少清单登记文件");
        }
    }
}
