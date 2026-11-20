using Otc.Publisher;
using Otc.Update.Core;
using Otc.Update.Core.Localization;

namespace Otc.UpdateTools.SelfCheck;

/// <summary>
/// 更新协议与命令行自检。
/// </summary>
internal static class UpdateProtocolSelfCheck
{
    /// <summary>
    /// 执行更新协议与命令行检查。
    /// </summary>
    /// <param name="context">检查上下文。</param>
    internal static void Run(SelfCheckContext context)
    {
        CheckLanguageRoundTrip(context);
        CheckPublisherCommandLine(context);
    }

    /// <summary>检查更新器启动参数能带上语言并原样解析回来。</summary>
    private static void CheckLanguageRoundTrip(SelfCheckContext context)
    {
        foreach (var language in new[]
                 {
                     LocalizationService.ChineseLanguage,
                     LocalizationService.EnglishLanguage,
                     LocalizationService.KoreanLanguage,
                 })
        {
            var options = new UpdateLaunchOptions
            {
                ProductCode = "CC_WPF",
                ProductType = "WCS",
                FromVersion = "1.0.1",
                ToVersion = "1.0.2",
                PackagePath = @"C:\Temp\CC_WPF_1.0.2.otcupdate",
                InstallDirectory = @"C:\Program Files\OTC\CC_WPF",
                ApplicationExecutable = "CC_WPF.exe",
                ParentProcessId = 1234,
                PackageSha256 = new string('a', 64),
                PackageSize = 1024,
                ManifestSignature = "signature",
                PublicKeyPem = "public-key",
                HealthTimeoutSeconds = 60,
                BackupRoot = @"C:\Backups",
                UpdateRoot = @"C:\Staging",
                Language = language,
            };

            var arguments = UpdateRuntimeFiles.ToArguments(options);
            var parsed = UpdateRuntimeFiles.ParseArguments(arguments);

            context.CheckEqual(
                $"更新器参数携带语言 {language}",
                parsed.Language,
                language);

            context.Check(
                $"更新器参数解析后仍保留产品编码 {language}",
                string.Equals(parsed.ProductCode, options.ProductCode, StringComparison.Ordinal));

            context.Check(
                $"更新器参数键值成对 {language}",
                arguments.Length % 2 == 0,
                $"参数个数 {arguments.Length} 不是偶数");
        }

        // 未携带语言时保持空串，由更新器按系统界面语言显示。
        var withoutLanguage = UpdateRuntimeFiles.ParseArguments(
            UpdateRuntimeFiles.ToArguments(new UpdateLaunchOptions()));

        context.Check(
            "未指定语言时更新器参数语言为空",
            string.IsNullOrWhiteSpace(withoutLanguage.Language));
    }

    /// <summary>检查发布命令行能解析语言参数。</summary>
    private static void CheckPublisherCommandLine(SelfCheckContext context)
    {
        var options = PublisherCommandLine.Parse(new[]
        {
            "--source", @"C:\Publish\app",
            "--updater-dir", @"C:\Publish\updater",
            "--product", "CC_WPF",
            "--type", "WCS",
            "--version", "1.0.2",
            "--share", @"\\fileserver\OTC_Updates",
            "--connection", "Host=127.0.0.1;Database=otcc",
            "--private-key", @"C:\Keys\update-private.pem",
            "--language", "ko-KR",
        });

        context.Check("发布命令行参数可解析", options is not null);

        if (options is null)
        {
            return;
        }

        context.CheckEqual("发布命令行解析出语言", options.Language, "ko-KR");

        var english = PublisherCommandLine.Parse(new[]
        {
            "--source", @"C:\Publish\app",
            "--product", "WMS_WPF",
            "--type", "WMS",
            "--version", "1.0.2",
            "--share", @"\\fileserver\OTC_Updates",
            "--connection", "Host=127.0.0.1;Database=otcc",
            "--private-key", @"C:\Keys\update-private.pem",
            "--language", "en-US",
        });

        context.Check(
            "发布命令行可解析英文语言",
            english is not null && string.Equals(english.Language, "en-US", StringComparison.Ordinal));

        context.Check(
            "命令行帮助包含语言参数",
            PublisherCommandLine.Usage.Contains("--language", StringComparison.Ordinal));
    }
}
