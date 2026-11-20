using Otc.Update.Core.Localization;
using System.Windows;

namespace Otc.Updater;

/// <summary>通用更新器应用入口。</summary>
public partial class App : Application
{
    /// <summary>解析后的启动参数。</summary>
    public static UpdateLaunchOptions? LaunchOptions { get; private set; }

    /// <summary>启动参数错误。</summary>
    public static string? StartupError { get; private set; }

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            LaunchOptions = e.Args.Length > 0
                ? UpdateRuntimeFiles.ParseArguments(e.Args)
                : null;
        }
        catch (Exception ex)
        {
            StartupError = ex.Message;
        }

        // 主程序会把当前界面语言带进来；未携带时按系统界面语言显示。
        ApplyLanguage(LaunchOptions?.Language);

        var window = new MainWindow(LaunchOptions, StartupError);
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// 应用界面语言。
    /// </summary>
    /// <param name="language">语言代码，可为空。</param>
    private static void ApplyLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        LocalizationService.Instance.SetLanguage(language);
    }
}
