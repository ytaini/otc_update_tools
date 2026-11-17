using Microsoft.Win32;
using Npgsql;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Otc.Update.Core;

namespace Otc.Publisher;

/// <summary>通用发布工具主窗口。</summary>
public partial class MainWindow : Window
{
    private const string DefaultVersion = "1.0.0";
    private const string DefaultShareDirectory = @"\\192.168.1.6\share";
    private readonly StringBuilder _log = new();

    /// <summary>初始化发布窗口。</summary>
    public MainWindow()
    {
        InitializeComponent();
        ApplyDefaults();
        var restored = ApplySavedSettings();
        UpdateTargetInputs();
        AppendLog(
            restored
                ? $"已恢复上次保存的发布配置：{PublisherSettingsStore.SettingsPath}"
                : "已载入当前项目的推荐默认值，请确认私钥和数据库口令后再发布。");
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Dispatcher.BeginInvoke(() =>
        {
            FormScrollViewer.ScrollToTop();
            ProductCodeBox.Focus();
        });
    }

    private void ApplyDefaults()
    {
        var workspaceRoot = FindWorkspaceRoot() ?? AppContext.BaseDirectory;
        var defaultProductCode = Environment.GetEnvironmentVariable("OTC_PUBLISHER_PRODUCT")?.Trim()
            ?? string.Empty;
        var defaultProductType = Environment.GetEnvironmentVariable("OTC_PUBLISHER_TYPE")?.Trim()
            ?? string.Empty;
        var productDirectory = string.IsNullOrWhiteSpace(defaultProductCode)
            ? "current"
            : $"{defaultProductCode}-{DefaultVersion}";
        var publishRoot = Path.Combine(
            workspaceRoot,
            "artifacts",
            "update-publish",
            productDirectory);

        SelectComboBoxText(ProductCodeBox, defaultProductCode);
        SelectComboBoxText(ProductTypeBox, defaultProductType);
        VersionBox.Text = DefaultVersion;
        TargetTypeBox.SelectedIndex = 0;
        SourceDirectoryBox.Text = Path.Combine(publishRoot, "app");
        UpdaterDirectoryBox.Text = Path.Combine(publishRoot, "updater");
        ShareDirectoryBox.Text = DefaultShareDirectory;
        PrivateKeyBox.Text = Path.Combine(workspaceRoot, "Keys", "update-private.pem");
        HostBox.Text = "127.0.0.1";
        PortBox.Text = "5432";
        DatabaseBox.Text = "otcc";
        UsernameBox.Text = "oapi";
        TargetValueBox.Text = string.Empty;
        RolloutPercentBox.Text = "100";
        MandatoryBox.IsChecked = false;
        RemarkBox.Text = "常规版本发布";
        StatusText.Text = "默认参数已预填，请确认后开始发布";
    }

    private bool ApplySavedSettings()
    {
        var settings = PublisherSettingsStore.Load();
        if (settings is null)
        {
            return false;
        }

        SelectComboBoxText(ProductCodeBox, settings.ProductCode);
        SelectComboBoxText(ProductTypeBox, settings.ProductType);
        VersionBox.Text = settings.Version;
        SelectComboBoxText(TargetTypeBox, settings.TargetType);
        SourceDirectoryBox.Text = settings.SourceDirectory;
        UpdaterDirectoryBox.Text = settings.UpdaterDirectory;
        ShareDirectoryBox.Text = settings.ShareDirectory;
        PrivateKeyBox.Text = settings.PrivateKeyPath;
        HostBox.Text = settings.DatabaseHost;
        PortBox.Text = settings.DatabasePort;
        DatabaseBox.Text = settings.DatabaseName;
        UsernameBox.Text = settings.DatabaseUsername;
        PasswordBox.Password = PublisherSettingsStore.UnprotectPassword(settings.ProtectedPassword);
        TargetValueBox.Text = settings.TargetValue;
        RolloutPercentBox.Text = settings.RolloutPercent;
        MandatoryBox.IsChecked = settings.Mandatory;
        RemarkBox.Text = settings.Remark;
        StatusText.Text = "已恢复上次保存的发布配置";
        return true;
    }

    private void OnBrowseSource(object sender, RoutedEventArgs e)
        => SelectFolder(SourceDirectoryBox);

    private void OnBrowseUpdater(object sender, RoutedEventArgs e)
        => SelectFolder(UpdaterDirectoryBox);

    private void OnBrowseShare(object sender, RoutedEventArgs e)
        => SelectFolder(ShareDirectoryBox);

    private void OnTargetTypeChanged(object sender, SelectionChangedEventArgs e)
        => UpdateTargetInputs();

    private void OnBrowsePrivateKey(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PEM key (*.pem)|*.pem|All files (*.*)|*.*",
            Title = "选择签名私钥",
        };
        var current = PrivateKeyBox.Text.Trim();
        if (File.Exists(current))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
            dialog.FileName = Path.GetFileName(current);
        }

        if (dialog.ShowDialog(this) == true)
        {
            PrivateKeyBox.Text = dialog.FileName;
        }
    }

    private void OnGenerateKeys(object sender, RoutedEventArgs e)
    {
        try
        {
            var current = PrivateKeyBox.Text.Trim();
            var dialog = new SaveFileDialog
            {
                Filter = "PEM key (*.pem)|*.pem",
                FileName = string.IsNullOrWhiteSpace(current)
                    ? "update-private.pem"
                    : Path.GetFileName(current),
                Title = "保存签名私钥",
            };

            if (!string.IsNullOrWhiteSpace(current))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(current);
            }

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var publicPath = Path.Combine(
                Path.GetDirectoryName(dialog.FileName)!,
                "update-public.pem");

            PublisherEngine.GenerateKeys(dialog.FileName, publicPath);
            PrivateKeyBox.Text = dialog.FileName;
            AppendLog($"私钥：{dialog.FileName}");
            AppendLog($"公钥：{publicPath}");
            StatusText.Text = "签名密钥已生成";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            LogExpander.IsExpanded = true;
            MessageBox.Show(
                this,
                $"密钥已生成。\n\n私钥：{dialog.FileName}\n公钥：{publicPath}",
                "生成成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "生成签名密钥失败";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            AppendLog(ex.Message);
            LogExpander.IsExpanded = true;
            MessageBox.Show(
                this,
                ex.Message,
                "生成失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnPublish(object sender, RoutedEventArgs e)
    {
        try
        {
            PublishButton.IsEnabled = false;
            GenerateKeyButton.IsEnabled = false;
            ProgressBar.IsIndeterminate = true;
            ProgressBar.Value = 0;
            StatusText.Text = "正在生成并发布更新包...";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");

            var options = BuildOptions();
            AppendLog(
                $"开始发布 {options.ProductCode} {options.Version}，范围 {options.TargetType}。");
            var result = await PublisherEngine.PublishAsync(
                options,
                message => Dispatcher.BeginInvoke(() => AppendLog(message)));

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            StatusText.Text = "发布成功，版本记录已写入数据库";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            AppendLog($"发布完成：{result.PackagePath}");
            AppendLog($"SHA-256：{result.Sha256}");
            MessageBox.Show(
                this,
                $"发布成功。\n\n{result.PackagePath}",
                "发布成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ProgressBar.IsIndeterminate = false;
            StatusText.Text = "发布失败，请展开日志查看原因";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            AppendLog(ex.Message);
            LogExpander.IsExpanded = true;
            MessageBox.Show(
                this,
                ex.Message,
                "发布失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            PublishButton.IsEnabled = true;
            GenerateKeyButton.IsEnabled = true;
        }
    }

    private PublisherOptions BuildOptions()
    {
        if (!int.TryParse(PortBox.Text, out var port)
            || !int.TryParse(RolloutPercentBox.Text, out var rollout))
        {
            throw new FormatException("端口和灰度百分比必须是整数");
        }

        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = HostBox.Text.Trim(),
            Port = port,
            Database = DatabaseBox.Text.Trim(),
            Username = UsernameBox.Text.Trim(),
            Password = PasswordBox.Password,
            SearchPath = "public",
            Timeout = 15,
            CommandTimeout = 30,
        }.ConnectionString;

        var targetType = (TargetTypeBox.SelectedItem as ComboBoxItem)
            ?.Content?.ToString() ?? UpdateTargetTypes.All;
        var targetValue = TargetValueBox.Text.Trim();

        if ((targetType == UpdateTargetTypes.Device || targetType == UpdateTargetTypes.Workshop)
            && string.IsNullOrWhiteSpace(targetValue))
        {
            throw new InvalidOperationException("按设备或车间发布时必须填写目标值");
        }

        return new PublisherOptions
        {
            SourceDirectory = SourceDirectoryBox.Text.Trim(),
            UpdaterDirectory = UpdaterDirectoryBox.Text.Trim(),
            ProductCode = ProductCodeBox.Text.Trim(),
            ProductType = ProductTypeBox.Text.Trim(),
            Version = VersionBox.Text.Trim(),
            ShareDirectory = ShareDirectoryBox.Text.Trim(),
            ConnectionString = connection,
            PrivateKeyPath = PrivateKeyBox.Text.Trim(),
            TargetType = targetType,
            TargetValue = targetValue,
            RolloutPercent = Math.Clamp(rollout, 0, 100),
            Mandatory = MandatoryBox.IsChecked == true,
            Remark = RemarkBox.Text.Trim(),
        };
    }

    private void UpdateTargetInputs()
    {
        if (TargetValueBox is null || RolloutPercentBox is null || TargetTypeBox is null)
        {
            return;
        }

        var targetType = (TargetTypeBox.SelectedItem as ComboBoxItem)
            ?.Content?.ToString() ?? UpdateTargetTypes.All;
        var needsTarget = targetType == UpdateTargetTypes.Device
            || targetType == UpdateTargetTypes.Workshop;
        var needsRollout = targetType == UpdateTargetTypes.Rollout;

        TargetValueBox.IsEnabled = needsTarget;
        RolloutPercentBox.IsEnabled = needsRollout;
    }

    private void SaveCurrentSettings()
    {
        var targetType = (TargetTypeBox.SelectedItem as ComboBoxItem)
            ?.Content?.ToString() ?? UpdateTargetTypes.All;

        PublisherSettingsStore.Save(
            ProductCodeBox.Text.Trim(),
            ProductTypeBox.Text.Trim(),
            VersionBox.Text.Trim(),
            targetType,
            SourceDirectoryBox.Text.Trim(),
            UpdaterDirectoryBox.Text.Trim(),
            ShareDirectoryBox.Text.Trim(),
            PrivateKeyBox.Text.Trim(),
            HostBox.Text.Trim(),
            PortBox.Text.Trim(),
            DatabaseBox.Text.Trim(),
            UsernameBox.Text.Trim(),
            PasswordBox.Password,
            TargetValueBox.Text.Trim(),
            RolloutPercentBox.Text.Trim(),
            MandatoryBox.IsChecked == true,
            RemarkBox.Text.Trim());
    }

    private void SelectFolder(TextBox target)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择目录",
        };

        if (Directory.Exists(target.Text))
        {
            dialog.InitialDirectory = target.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            target.Text = dialog.FolderName;
        }
    }

    private void AppendLog(string message)
    {
        _log.AppendLine($"{DateTime.Now:HH:mm:ss} {message}");
        LogText.Text = _log.ToString();
        LogText.ScrollToEnd();
    }

    private static void SelectComboBoxText(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.Text = value;
    }

    private static string? FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Otc.Publisher", "Otc.Publisher.csproj")))
            {
                return directory.Parent?.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <inheritdoc />
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            SaveCurrentSettings();
        }
        catch
        {
            // 关闭阶段不阻断退出；用户也可通过“保存配置”获得明确失败提示。
        }

        base.OnClosing(e);
    }
}
