using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Otc.Publisher;

/// <summary>Publisher 上次保存的本机配置。</summary>
internal sealed class PublisherSettings
{
    /// <summary>产品编码。</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>系统类型。</summary>
    public string ProductType { get; set; } = string.Empty;

    /// <summary>版本号。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>发布范围。</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>应用发布目录。</summary>
    public string SourceDirectory { get; set; } = string.Empty;

    /// <summary>Updater 目录。</summary>
    public string UpdaterDirectory { get; set; } = string.Empty;

    /// <summary>共享或 HTTPS 发布根目录。</summary>
    public string ShareDirectory { get; set; } = string.Empty;

    /// <summary>签名私钥路径。</summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    /// <summary>数据库服务器。</summary>
    public string DatabaseHost { get; set; } = string.Empty;

    /// <summary>数据库端口。</summary>
    public string DatabasePort { get; set; } = string.Empty;

    /// <summary>数据库名称。</summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>数据库用户。</summary>
    public string DatabaseUsername { get; set; } = string.Empty;

    /// <summary>DPAPI 加密后的数据库口令。</summary>
    public string ProtectedPassword { get; set; } = string.Empty;

    /// <summary>目标设备或车间。</summary>
    public string TargetValue { get; set; } = string.Empty;

    /// <summary>灰度百分比。</summary>
    public string RolloutPercent { get; set; } = string.Empty;

    /// <summary>是否强制更新。</summary>
    public bool Mandatory { get; set; }

    /// <summary>发布说明。</summary>
    public string Remark { get; set; } = string.Empty;
}

/// <summary>Publisher 配置的本机持久化与口令保护。</summary>
internal static class PublisherSettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OTC.Publisher.Settings.v1");

    /// <summary>配置文件路径。</summary>
    internal static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OTC",
        "Otc.Publisher",
        "publisher-settings.json");

    /// <summary>
    /// 语言偏好文件路径。
    /// </summary>
    /// <remarks>
    /// 语言偏好单独存放，不与发布配置写同一个文件：
    /// 用户只切换语言时不必触碰发布配置，也不会因为语言文件先于配置存在
    /// 而把发布表单恢复成空值。
    /// </remarks>
    internal static string LanguageSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OTC",
        "Otc.Publisher",
        "publisher-language.json");

    /// <summary>读取本机配置；不存在或损坏时返回 null。</summary>
    internal static PublisherSettings? Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<PublisherSettings>(
                File.ReadAllText(SettingsPath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>保存本机配置。</summary>
    internal static void Save(
        string productCode,
        string productType,
        string version,
        string targetType,
        string sourceDirectory,
        string updaterDirectory,
        string shareDirectory,
        string privateKeyPath,
        string databaseHost,
        string databasePort,
        string databaseName,
        string databaseUsername,
        string databasePassword,
        string targetValue,
        string rolloutPercent,
        bool mandatory,
        string remark)
    {
        var settings = new PublisherSettings
        {
            ProductCode = productCode,
            ProductType = productType,
            Version = version,
            TargetType = targetType,
            SourceDirectory = sourceDirectory,
            UpdaterDirectory = updaterDirectory,
            ShareDirectory = shareDirectory,
            PrivateKeyPath = privateKeyPath,
            DatabaseHost = databaseHost,
            DatabasePort = databasePort,
            DatabaseName = databaseName,
            DatabaseUsername = databaseUsername,
            ProtectedPassword = Protect(databasePassword),
            TargetValue = targetValue,
            RolloutPercent = rolloutPercent,
            Mandatory = mandatory,
            Remark = remark,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// 读取本机保存的语言偏好。
    /// </summary>
    /// <returns>语言代码；未保存或损坏时返回 null。</returns>
    internal static string? LoadLanguage()
    {
        try
        {
            if (!File.Exists(LanguageSettingsPath))
            {
                return null;
            }

            var language = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(LanguageSettingsPath));

            if (language is null
                || !language.TryGetValue("language", out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 保存本机语言偏好。
    /// </summary>
    /// <param name="language">语言代码。</param>
    internal static void SaveLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LanguageSettingsPath)!);
            File.WriteAllText(
                LanguageSettingsPath,
                JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["language"] = language },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
            // 语言偏好不是关键数据，写入失败不影响本次语言切换。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>解密本机保存的数据库口令。</summary>
    internal static string UnprotectPassword(string protectedPassword)
    {
        if (string.IsNullOrWhiteSpace(protectedPassword))
        {
            return string.Empty;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedPassword),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            Entropy,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }
}
