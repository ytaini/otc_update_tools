using Otc.Update.Core;
using Otc.Update.Core.Localization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Otc.UpdateTools.SelfCheck;

/// <summary>
/// 三语本地化自检。
/// </summary>
internal static class LocalizationSelfCheck
{
    private static readonly string[] Languages =
    {
        LocalizationService.ChineseLanguage,
        LocalizationService.EnglishLanguage,
        LocalizationService.KoreanLanguage,
    };

    /// <summary>
    /// 允许三语取值完全相同的词条。
    /// </summary>
    /// <remarks>
    /// 这些词条是协议名或命令行契约，不属于需要翻译的自然语言：
    /// <c>Publisher.Badge.Transport</c> 是传输协议名（UNC / HTTPS）；
    /// <c>Publisher.Cli.Usage</c> 是命令行帮助，选项名必须与脚本约定一致，
    /// 只保留结构不翻译。
    /// </remarks>
    private static readonly HashSet<string> SharedValueKeys = new(StringComparer.Ordinal)
    {
        "Publisher.Badge.Transport",
        "Publisher.Cli.Usage",
    };

    /// <summary>
    /// 执行全部本地化检查。
    /// </summary>
    /// <param name="context">检查上下文。</param>
    /// <param name="repositoryRoot">仓库根目录，用于扫描 XAML 与源码。</param>
    internal static void Run(SelfCheckContext context, string repositoryRoot)
    {
        var catalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var language in Languages)
        {
            catalogs[language] = ReadCatalog(language);
        }

        context.Check(
            "三语词条文件均可读取",
            catalogs.Values.All(catalog => catalog.Count > 0),
            string.Join(", ", catalogs.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key)));

        var chinese = catalogs[Languages[0]];
        var english = catalogs[Languages[1]];
        var korean = catalogs[Languages[2]];

        context.CheckEqual("中英韩词条数量一致", chinese.Count, english.Count);
        context.CheckEqual("中英韩词条数量一致（韩文）", chinese.Count, korean.Count);

        var missingInEnglish = chinese.Keys.Where(key => !english.ContainsKey(key)).OrderBy(key => key).ToArray();
        var missingInKorean = chinese.Keys.Where(key => !korean.ContainsKey(key)).OrderBy(key => key).ToArray();
        var extraInEnglish = english.Keys.Where(key => !chinese.ContainsKey(key)).OrderBy(key => key).ToArray();
        var extraInKorean = korean.Keys.Where(key => !chinese.ContainsKey(key)).OrderBy(key => key).ToArray();

        context.Check("英文词条无缺键", missingInEnglish.Length == 0, string.Join(", ", missingInEnglish));
        context.Check("韩文词条无缺键", missingInKorean.Length == 0, string.Join(", ", missingInKorean));
        context.Check("英文词条无多余键", extraInEnglish.Length == 0, string.Join(", ", extraInEnglish));
        context.Check("韩文词条无多余键", extraInKorean.Length == 0, string.Join(", ", extraInKorean));

        // 词条不能等于键本身，否则说明漏翻或键名写错。
        foreach (var language in Languages)
        {
            var untranslated = catalogs[language]
                .Where(pair => string.Equals(pair.Key, pair.Value, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .OrderBy(key => key)
                .ToArray();

            context.Check(
                $"{language} 词条均已翻译",
                untranslated.Length == 0,
                string.Join(", ", untranslated));
        }

        // 三种语言的取值不能完全相同，避免复制粘贴漏翻。
        foreach (var key in chinese.Keys)
        {
            if (SharedValueKeys.Contains(key))
            {
                continue;
            }

            var values = new[] { chinese[key], english[key], korean[key] }
                .Distinct(StringComparer.Ordinal)
                .Count();

            context.Check(
                $"词条 {key} 三语取值不重复",
                values > 1,
                "三语取值完全相同，疑似漏翻");
        }

        CheckLanguageSwitch(context);
        CheckReferencedKeys(context, repositoryRoot);
    }

    /// <summary>检查语言切换与区域设置。</summary>
    private static void CheckLanguageSwitch(SelfCheckContext context)
    {
        var service = LocalizationService.Instance;
        var original = service.CurrentLanguage;

        context.Check(
            "支持中文、英文、韩文三种语言",
            service.SupportedLanguages.Count == 3
            && service.SupportedLanguages.Select(option => option.Code)
                .OrderBy(code => code, StringComparer.Ordinal)
                .SequenceEqual(new[] { "en-US", "ko-KR", "zh-CN" }),
            string.Join(", ", service.SupportedLanguages.Select(option => option.Code)));

        foreach (var language in Languages)
        {
            var switched = service.SetLanguage(language);
            context.CheckEqual($"切换到 {language}", service.CurrentLanguage, language);
            context.Check($"切换到 {language} 生效", switched);
            context.Check(
                $"切换到 {language} 后区域文化同步",
                System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith(
                    language[..2],
                    StringComparison.OrdinalIgnoreCase),
                System.Globalization.CultureInfo.CurrentUICulture.Name);
        }

        context.CheckEqual(
            "语言名称归一化：ko",
            LocalizationService.ResolveSupportedLanguage("ko-KR"),
            LocalizationService.KoreanLanguage);
        context.CheckEqual(
            "语言名称归一化：en",
            LocalizationService.ResolveSupportedLanguage("en-US"),
            LocalizationService.EnglishLanguage);
        context.CheckEqual(
            "语言名称归一化：zh",
            LocalizationService.ResolveSupportedLanguage("zh-CN"),
            LocalizationService.ChineseLanguage);
        context.CheckEqual(
            "语言名称归一化：空值回落中文",
            LocalizationService.ResolveSupportedLanguage(null),
            LocalizationService.ChineseLanguage);
        context.CheckEqual(
            "语言名称归一化：未知语言回落中文",
            LocalizationService.ResolveSupportedLanguage("fr-FR"),
            LocalizationService.ChineseLanguage);

        service.SetLanguage(original);
    }

    /// <summary>检查 XAML 与源码里引用的词条键都存在。</summary>
    private static void CheckReferencedKeys(SelfCheckContext context, string repositoryRoot)
    {
        var catalog = ReadCatalog(Languages[0]);

        var xamlFiles = Directory
            .EnumerateFiles(repositoryRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        var missingXamlKeys = new List<string>();

        foreach (var file in xamlFiles)
        {
            var text = File.ReadAllText(file);

            foreach (Match match in LocalizationKeyScanner.XamlKeyPattern.Matches(text))
            {
                var key = match.Groups["key"].Value;

                if (!catalog.ContainsKey(key))
                {
                    missingXamlKeys.Add($"{Path.GetFileName(file)}:{key}");
                }
            }
        }

        context.Check(
            "XAML 引用的词条键全部存在",
            missingXamlKeys.Count == 0,
            string.Join(", ", missingXamlKeys));

        var sourceFiles = Directory
            .EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.EndsWith("SelfCheckContext.cs", StringComparison.Ordinal))
            .ToArray();

        var missingCodeKeys = new List<string>();

        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);

            foreach (Match match in LocalizationKeyScanner.IndexerPattern.Matches(text))
            {
                var key = match.Groups["key"].Value;

                if (!catalog.ContainsKey(key))
                {
                    missingCodeKeys.Add($"{Path.GetFileName(file)}:{key}");
                }
            }

            foreach (Match match in LocalizationKeyScanner.FormatPattern.Matches(text))
            {
                var key = match.Groups["key"].Value;

                if (!catalog.ContainsKey(key))
                {
                    missingCodeKeys.Add($"{Path.GetFileName(file)}:{key}");
                }
            }
        }

        context.Check(
            "源码引用的词条键全部存在",
            missingCodeKeys.Count == 0,
            string.Join(", ", missingCodeKeys));

        context.Check(
            "XAML 使用统一的本地化命名空间",
            xamlFiles
                .Where(path => File.ReadAllText(path).Contains("loc:Loc", StringComparison.Ordinal))
                .All(path => File.ReadAllText(path)
                    .Contains("http://schemas.otc.com/localization", StringComparison.Ordinal)),
            "存在仍使用 clr-namespace 写法的 XAML");
    }

    /// <summary>
    /// 读取指定语言的嵌入词条。
    /// </summary>
    /// <param name="language">语言代码。</param>
    /// <returns>词条字典。</returns>
    private static Dictionary<string, string> ReadCatalog(string language)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith($".{language}.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var reader = new StreamReader(stream);

        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
