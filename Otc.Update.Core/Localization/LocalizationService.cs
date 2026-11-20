using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Otc.Update.Core.Localization;

/// <summary>
/// 基于嵌入 JSON 词条的三语本地化服务。
/// </summary>
/// <remarks>
/// Publisher 与 Updater 共用同一个进程内单例，使 XAML 标记扩展、代码取词与
/// 命令行模式共享同一语言状态。词条随程序集以嵌入资源发布，
/// 保证工具被复制到现场目录后仍能正确显示当前语言。
/// </remarks>
public sealed class LocalizationService : INotifyPropertyChanged
{
    /// <summary>默认中文语言代码。</summary>
    public const string ChineseLanguage = "zh-CN";

    /// <summary>英文语言代码。</summary>
    public const string EnglishLanguage = "en-US";

    /// <summary>韩文语言代码。</summary>
    public const string KoreanLanguage = "ko-KR";

    /// <summary>开发期词条覆盖目录环境变量名。</summary>
    private const string OverrideDirectoryVariable = "OTC_UPDATE_LOCALIZATION_DIR";

    /// <summary>进程内共享实例。</summary>
    private static readonly Lazy<LocalizationService> SharedInstance = new(() => new LocalizationService());

    /// <summary>当前语言词条。</summary>
    private IReadOnlyDictionary<string, string> _currentTexts =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>初始化本地化服务并按系统界面语言载入词条。</summary>
    private LocalizationService()
    {
        SupportedLanguages = new[]
        {
            new LanguageOption(ChineseLanguage, "中文", "中"),
            new LanguageOption(EnglishLanguage, "English", "EN"),
            new LanguageOption(KoreanLanguage, "한국어", "한"),
        };

        SetLanguage(CultureInfo.CurrentUICulture.Name);
    }

    /// <summary>进程内共享实例。</summary>
    public static LocalizationService Instance => SharedInstance.Value;

    /// <summary>当前语言代码。</summary>
    public string CurrentLanguage { get; private set; } = ChineseLanguage;

    /// <summary>受支持的语言列表。</summary>
    public IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    /// <summary>语言切换完成事件。</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>索引器属性变化事件，供 WPF 绑定刷新全部本地化文本。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 取词条文本；当前语言缺词时回落到中文，再缺则返回键本身。
    /// </summary>
    /// <param name="key">词条键。</param>
    /// <returns>词条文本。</returns>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (_currentTexts.TryGetValue(key, out var value))
            {
                return value;
            }

            return LoadTexts(ChineseLanguage).TryGetValue(key, out var fallback)
                ? fallback
                : key;
        }
    }

    /// <summary>
    /// 切换语言并刷新界面。
    /// </summary>
    /// <param name="languageCode">语言代码或语言名称。</param>
    /// <returns>切换成功返回 true。</returns>
    public bool SetLanguage(string? languageCode)
    {
        var normalized = ResolveSupportedLanguage(languageCode);
        var texts = LoadTexts(normalized);

        if (texts.Count == 0)
        {
            return false;
        }

        var changed = !string.Equals(CurrentLanguage, normalized, StringComparison.OrdinalIgnoreCase)
            || !_currentTexts.SequenceEqual(texts);

        _currentTexts = texts;
        CurrentLanguage = normalized;

        var culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (changed)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>
    /// 取词条并按当前区域格式化。
    /// </summary>
    /// <param name="key">词条键。</param>
    /// <param name="args">格式化参数。</param>
    /// <returns>格式化后的文本。</returns>
    public string Format(string key, params object?[] args)
        => args.Length == 0
            ? this[key]
            : string.Format(CultureInfo.CurrentCulture, this[key], args);

    /// <summary>
    /// 把任意语言名称归一化为受支持的语言代码。
    /// </summary>
    /// <param name="languageCode">语言名称或区域代码。</param>
    /// <returns>受支持的语言代码。</returns>
    public static string ResolveSupportedLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return ChineseLanguage;
        }

        if (languageCode.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
        {
            return KoreanLanguage;
        }

        if (languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return EnglishLanguage;
        }

        return ChineseLanguage;
    }

    /// <summary>
    /// 读取指定语言的词条。
    /// </summary>
    /// <remarks>
    /// 优先读取开发期覆盖目录，其次读取嵌入资源。嵌入资源随程序集发布，
    /// 保证现场目录里的工具副本不会因为缺少词条文件而回退到键名。
    /// </remarks>
    /// <param name="languageCode">语言区域代码。</param>
    /// <returns>词条字典；读取失败返回空字典。</returns>
    private static IReadOnlyDictionary<string, string> LoadTexts(string languageCode)
    {
        foreach (var json in EnumerateTextSources(languageCode))
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            try
            {
                var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (catalog is not null && catalog.Count > 0)
                {
                    return new Dictionary<string, string>(catalog, StringComparer.Ordinal);
                }
            }
            catch (JsonException)
            {
                // 词条文件损坏时继续尝试下一个来源，最终回落到键名。
            }
            catch (IOException)
            {
            }
        }

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// 枚举指定语言可用的词条文本来源。
    /// </summary>
    /// <param name="languageCode">语言区域代码。</param>
    /// <returns>词条文本序列。</returns>
    private static IEnumerable<string> EnumerateTextSources(string languageCode)
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(OverrideDirectoryVariable);

        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            var overridePath = Path.Combine(overrideDirectory, $"{languageCode}.json");

            if (File.Exists(overridePath))
            {
                yield return File.ReadAllText(overridePath);
            }
        }

        var embedded = ReadEmbeddedText(languageCode);

        if (!string.IsNullOrWhiteSpace(embedded))
        {
            yield return embedded;
        }
    }

    /// <summary>
    /// 从程序集嵌入资源读取语言词条。
    /// </summary>
    /// <param name="languageCode">语言区域代码。</param>
    /// <returns>JSON 文本；资源不存在时返回 null。</returns>
    private static string? ReadEmbeddedText(string languageCode)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var suffix = $".{languageCode}.json";
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
