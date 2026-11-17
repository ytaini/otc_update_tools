using System.Windows;
using System.IO;

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
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
