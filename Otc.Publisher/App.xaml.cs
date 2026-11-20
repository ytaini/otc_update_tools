using System.Windows;
using System.IO;
using Otc.Update.Core.Localization;

namespace Otc.Publisher;

/// <summary>通用发布工具入口。</summary>
public partial class App : Application
{
    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length == 3
            && e.Args[0].Equals("--generate-key", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                PublisherEngine.GenerateKeys(e.Args[1], e.Args[2]);
                Shutdown(0);
            }
            catch
            {
                Shutdown(2);
            }

            return;
        }

        if (e.Args.Length > 0)
        {
            var options = PublisherCommandLine.Parse(e.Args);

            // 命令行模式优先按 --language 切换界面语言，未指定时沿用系统语言。
            ApplyCommandLineLanguage(options?.Language);

            if (options is null)
            {
                File.WriteAllText(
                    Path.Combine(AppContext.BaseDirectory, "publisher-cli-result.log"),
                    PublisherCommandLine.Usage);
                Shutdown(1);
                return;
            }

            try
            {
                var result = await PublisherEngine.PublishAsync(options);
                File.WriteAllText(
                    Path.Combine(AppContext.BaseDirectory, "publisher-cli-result.log"),
                    $"Published {options.ProductCode} {options.Version}{Environment.NewLine}" +
                    $"Package: {result.PackagePath}{Environment.NewLine}" +
                    $"SHA-256: {result.Sha256}{Environment.NewLine}" +
                    $"Size: {result.FileSize}");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(
                    Path.Combine(AppContext.BaseDirectory, "publisher-cli-result.log"),
                    ex.ToString());
                Shutdown(2);
            }

            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // 图形界面模式：优先恢复本机保存的语言偏好，未保存时沿用系统语言。
        ApplySavedLanguage();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// 按命令行参数或环境变量切换界面语言。
    /// </summary>
    /// <param name="language">命令行 <c>--language</c> 取值，可为空。</param>
    private static void ApplyCommandLineLanguage(string? language)
    {
        var resolved = !string.IsNullOrWhiteSpace(language)
            ? language
            : Environment.GetEnvironmentVariable("OTC_PUBLISHER_LANGUAGE");

        if (!string.IsNullOrWhiteSpace(resolved))
        {
            LocalizationService.Instance.SetLanguage(resolved);
        }
    }

    /// <summary>恢复本机保存的语言偏好。</summary>
    private static void ApplySavedLanguage()
    {
        var saved = PublisherSettingsStore.LoadLanguage();

        if (!string.IsNullOrWhiteSpace(saved))
        {
            LocalizationService.Instance.SetLanguage(saved);
        }
    }
}
