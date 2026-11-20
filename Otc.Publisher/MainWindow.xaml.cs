using Microsoft.Win32;
using Npgsql;
using Otc.Update.Core;
using Otc.Update.Core.Localization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Otc.Publisher;

/// <summary>通用发布工具主窗口。</summary>
public partial class MainWindow : Window
{
    private const string DefaultVersion = "1.0.0";
    private const string DefaultShareDirectory = @"\\192.168.1.6\share";
    private readonly StringBuilder _log = new();

    /// <summary>
    /// 当前状态栏使用的词条键。
    /// </summary>
    /// <remarks>
    /// 状态栏文本是命令式赋值，会覆盖 XAML 上的绑定；
    /// 记录键值后，切换语言时重新套用一次即可让状态栏同步刷新。
    /// </remarks>
    private string? _statusKey;
    private object?[] _statusArgs = Array.Empty<object?>();

    /// <summary>初始化发布窗口。</summary>
    public MainWindow()
    {
        InitializeComponent();
        InitializeLanguageSelector();
        ApplyDefaults();
        var restored = ApplySavedSettings();
        UpdateTargetInputs();
        AppendLog(
            restored
                ? Loc.Format("Publisher.Status.RestoredWithPath", PublisherSettingsStore.SettingsPath)
                : Loc["Publisher.Status.DefaultsLoaded"]);
        Loaded += OnLoaded;
    }

    /// <summary>本地化服务快捷访问。</summary>
    private static LocalizationService Loc => LocalizationService.Instance;

    /// <summary>初始化页头语言下拉框。</summary>
    private void InitializeLanguageSelector()
    {
        LanguageBox.ItemsSource = Loc.SupportedLanguages;
        LanguageBox.DisplayMemberPath = nameof(LanguageOption.DisplayName);
        LanguageBox.SelectedItem = Loc.SupportedLanguages
            .FirstOrDefault(option => string.Equals(
                option.Code,
                Loc.CurrentLanguage,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>页头语言切换事件。</summary>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is not LanguageOption option)
        {
            return;
        }

        if (Loc.SetLanguage(option.Code))
        {
            // 词条绑定由本地化服务统一刷新；此处同步当前选中项、状态栏与落盘的语言偏好。
            LanguageBox.SelectedItem = Loc.SupportedLanguages
                .FirstOrDefault(item => string.Equals(
                    item.Code,
                    Loc.CurrentLanguage,
                    StringComparison.OrdinalIgnoreCase));
            RefreshStatusText();
            SaveLanguagePreference();
        }
    }

    /// <summary>
    /// 设置状态栏文本并记录词条键。
    /// </summary>
    /// <param name="key">词条键。</param>
    /// <param name="args">格式化参数。</param>
    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusText.Text = args.Length == 0 ? Loc[key] : Loc.Format(key, args);
    }

    /// <summary>按当前语言重新套用状态栏文本。</summary>
    private void RefreshStatusText()
    {
        if (_statusKey is null)
        {
            return;
        }

        StatusText.Text = _statusArgs.Length == 0
            ? Loc[_statusKey]
            : Loc.Format(_statusKey, _statusArgs);
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
        var workspaceRoot = Directory.GetCurrentDirectory();
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
        PrivateKeyBox.Text = string.Empty;
        HostBox.Text = "127.0.0.1";
        PortBox.Text = "5432";
        DatabaseBox.Text = "otcc";
        UsernameBox.Text = "oapi";
        TargetValueBox.Text = string.Empty;
        RolloutPercentBox.Text = "100";
        MandatoryBox.IsChecked = false;
        RemarkBox.Text = Loc["Publisher.Default.Remark"];
        SetStatus("Publisher.Status.DefaultsReady");
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
        SetStatus("Publisher.Status.Restored");
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
            Title = Loc["Publisher.Dialog.SelectPrivateKey"],
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
                Title = Loc["Publisher.Dialog.SavePrivateKey"],
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
            AppendLog(Loc.Format("Publisher.Log.PrivateKey", dialog.FileName));
            AppendLog(Loc.Format("Publisher.Log.PublicKey", publicPath));
            SetStatus("Publisher.Status.KeysGenerated");
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            LogExpander.IsExpanded = true;
            MessageBox.Show(
                this,
                Loc.Format("Publisher.Message.KeysGenerated", dialog.FileName, publicPath),
                Loc["Publisher.Dialog.KeysGenerated"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus("Publisher.Status.KeysFailed");
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            AppendLog(ex.Message);
            LogExpander.IsExpanded = true;
            MessageBox.Show(
                this,
                ex.Message,
                Loc["Publisher.Dialog.KeysFailed"],
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
            SetStatus("Publisher.Status.Publishing");
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");

            var options = BuildOptions();
            AppendLog(Loc.Format(
                "Publisher.Log.Start",
                options.ProductCode,
                options.Version,
                options.TargetType));
            var result = await PublisherEngine.PublishAsync(
                options,
                message => Dispatcher.BeginInvoke(() => AppendLog(message)));

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            SetStatus("Publisher.Status.Success");
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            AppendLog(Loc.Format("Publisher.Log.Completed", result.PackagePath));
            AppendLog(Loc.Format("Publisher.Log.Sha256", result.Sha256));
            MessageBox.Show(
                this,
                result.PackagePath,
                Loc["Publisher.Dialog.PublishSuccess"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ProgressBar.IsIndeterminate = false;
            SetStatus("Publisher.Status.Failed");
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            AppendLog(ex.Message);
            LogExpander.IsExpanded = true;
            MessageBox.Show(
                this,
                ex.Message,
                Loc["Publisher.Dialog.PublishFailed"],
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
            throw new FormatException(Loc["Publisher.Error.PortAndRollout"]);
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
            throw new InvalidOperationException(Loc["Publisher.Error.TargetValueRequired"]);
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

    /// <summary>
    /// 把当前语言偏好写到本机文件。
    /// </summary>
    /// <remarks>
    /// 语言偏好单独存放，不影响发布配置；写入失败不打断当前会话的语言切换。
    /// </remarks>
    private void SaveLanguagePreference()
    {
        try
        {
            PublisherSettingsStore.SaveLanguage(Loc.CurrentLanguage);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
            Title = Loc["Publisher.Dialog.SelectFolder"],
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
