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

        var window = new MainWindow(LaunchOptions, StartupError);
        MainWindow = window;
        window.Show();
    }
}
