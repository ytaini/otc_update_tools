using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Otc.Update.Core;

/// <summary>健康标记、更新结果和更新器参数的持久化。</summary>
public static class UpdateRuntimeFiles
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>默认更新数据根目录。</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OTC",
        "Updates");

    /// <summary>获取产品健康标记文件路径。</summary>
    public static string GetHealthFilePath(string root, string productCode)
        => Path.Combine(root, "health", $"{Sanitize(productCode)}.json");

    /// <summary>获取产品最近更新结果文件路径。</summary>
    public static string GetResultFilePath(string root, string productCode)
        => Path.Combine(root, "results", $"{Sanitize(productCode)}-last-update.json");

    /// <summary>写入健康标记。</summary>
    public static void WriteHealthMarker(string root, UpdateHealthMarker marker)
        => WriteJsonAtomic(GetHealthFilePath(root, marker.ProductCode), marker);

    /// <summary>读取健康标记。</summary>
    public static UpdateHealthMarker? ReadHealthMarker(string root, string productCode)
        => ReadJson<UpdateHealthMarker>(GetHealthFilePath(root, productCode));

    /// <summary>写入更新结果。</summary>
    public static void WriteResult(string root, UpdateResult result)
        => WriteJsonAtomic(GetResultFilePath(root, result.ProductCode), result);

    /// <summary>读取更新结果。</summary>
    public static UpdateResult? ReadResult(string root, string productCode)
        => ReadJson<UpdateResult>(GetResultFilePath(root, productCode));

    /// <summary>删除已上报的结果文件。</summary>
    public static void DeleteResult(string root, string productCode)
    {
        var path = GetResultFilePath(root, productCode);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>把更新器参数编码成命令行参数。</summary>
    public static string[] ToArguments(UpdateLaunchOptions options)
        => new[]
        {
            "--product", options.ProductCode,
            "--product-type", options.ProductType,
            "--from", options.FromVersion,
            "--to", options.ToVersion,
            "--package", options.PackagePath,
            "--install-dir", options.InstallDirectory,
            "--app-exe", options.ApplicationExecutable,
            "--parent-pid", options.ParentProcessId.ToString(),
            "--sha256", options.PackageSha256,
            "--size", options.PackageSize.ToString(),
            "--signature", options.ManifestSignature,
            "--public-key", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(options.PublicKeyPem)),
            "--health-timeout", options.HealthTimeoutSeconds.ToString(),
            "--backup-root", options.BackupRoot,
            "--update-root", options.UpdateRoot,
        };

    /// <summary>解析更新器命令行参数。</summary>
    public static UpdateLaunchOptions ParseArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index + 1 < args.Length; index += 2)
        {
            values[args[index]] = args[index + 1];
        }

        return new UpdateLaunchOptions
        {
            ProductCode = Get(values, "--product"),
            ProductType = Get(values, "--product-type"),
            FromVersion = Get(values, "--from"),
            ToVersion = Get(values, "--to"),
            PackagePath = Get(values, "--package"),
            InstallDirectory = Get(values, "--install-dir"),
            ApplicationExecutable = Get(values, "--app-exe"),
            ParentProcessId = int.TryParse(Get(values, "--parent-pid"), out var pid) ? pid : 0,
            PackageSha256 = Get(values, "--sha256"),
            PackageSize = long.TryParse(Get(values, "--size"), out var size) ? size : 0,
            ManifestSignature = Get(values, "--signature"),
            PublicKeyPem = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(Get(values, "--public-key"))),
            HealthTimeoutSeconds = int.TryParse(
                Get(values, "--health-timeout"),
                out var timeout) ? Math.Max(timeout, 10) : 60,
            BackupRoot = Get(values, "--backup-root"),
            UpdateRoot = Get(values, "--update-root"),
        };
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : string.Empty;

    private static string Sanitize(string value)
        => string.Concat(value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";

        using (var stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, value, JsonOptions);
        }

        File.Move(temporary, path, true);
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch (IOException)
        {
            return default;
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
