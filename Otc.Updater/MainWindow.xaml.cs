using Otc.Update.Core.Localization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Otc.Updater;

/// <summary>更新进度窗口。</summary>
public partial class MainWindow : Window
{
    private readonly UpdateLaunchOptions? _options;
    private readonly StringBuilder _log = new();
    private bool _finished;

    /// <summary>
    /// 当前状态标题与页脚提示使用的词条键。
    /// </summary>
    /// <remarks>
    /// 这两处文本是命令式赋值，会覆盖 XAML 上的绑定；
    /// 记录键值后，切换语言时重新套用一次即可让界面同步刷新。
    /// </remarks>
    private string? _statusKey;
    private object?[] _statusArgs = Array.Empty<object?>();
    private string? _footerKey;

    /// <summary>初始化更新窗口。</summary>
    public MainWindow(UpdateLaunchOptions? options, string? startupError)
    {
        InitializeComponent();
        _options = options;
        InitializeLanguageSelector();

        if (options is null)
        {
            UpdateContent.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            FooterHintText.Text = startupError ?? Loc["Updater.Empty.Hint"];
            CloseButton.IsEnabled = true;
            _finished = true;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        UpdateContent.Visibility = Visibility.Visible;
        ProductText.Text = $"{options.ProductCode} / {options.ProductType}";
        VersionText.Text = $"{options.FromVersion}  →  {options.ToVersion}";
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
    /// <remarks>
    /// 更新过程中的状态文案由本地化服务统一刷新；
    /// 已写入日志文件的历史行保留切换前的语言，便于现场排查。
    /// </remarks>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is not LanguageOption option)
        {
            return;
        }

        if (Loc.SetLanguage(option.Code))
        {
            LanguageBox.SelectedItem = Loc.SupportedLanguages
                .FirstOrDefault(item => string.Equals(
                    item.Code,
                    Loc.CurrentLanguage,
                    StringComparison.OrdinalIgnoreCase));
            RefreshLocalizedTexts();
        }
    }

    /// <summary>
    /// 设置状态标题并记录词条键。
    /// </summary>
    /// <param name="key">词条键。</param>
    /// <param name="args">格式化参数。</param>
    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusText.Text = args.Length == 0 ? Loc[key] : Loc.Format(key, args);
    }

    /// <summary>
    /// 设置页脚提示并记录词条键。
    /// </summary>
    /// <param name="key">词条键。</param>
    private void SetFooterHint(string key)
    {
        _footerKey = key;
        FooterHintText.Text = Loc[key];
    }

    /// <summary>按当前语言重新套用状态标题与页脚提示。</summary>
    private void RefreshLocalizedTexts()
    {
        if (_statusKey is not null)
        {
            StatusText.Text = _statusArgs.Length == 0
                ? Loc[_statusKey]
                : Loc.Format(_statusKey, _statusArgs);
        }

        if (_footerKey is not null)
        {
            FooterHintText.Text = Loc[_footerKey];
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (_options is null)
        {
            return;
        }

        var progress = new Progress<UpdateProgress>(value =>
        {
            ProgressBar.Value = value.Percent;
            ProgressPercentText.Text = $"{value.Percent:F0}%";
            StatusText.Text = value.Status;
            StatusText.Foreground = value.IsError
                ? (System.Windows.Media.Brush)FindResource("DangerBrush")
                : (System.Windows.Media.Brush)FindResource("AccentBrush");
            StatusDetailText.Text = string.IsNullOrWhiteSpace(value.Log)
                ? value.Status
                : value.Log;
            UpdateStages(value.Percent, value.IsError);

            if (!string.IsNullOrWhiteSpace(value.Log))
            {
                _log.AppendLine($"{DateTime.Now:HH:mm:ss} {value.Log}");
                LogText.Text = _log.ToString();
                LogText.ScrollToEnd();
            }

            if (value.IsError)
            {
                LogExpander.IsExpanded = true;
            }
        });

        try
        {
            var result = await UpdateRunner.RunAsync(_options, progress);
            _finished = true;
            CloseButton.IsEnabled = true;
            ProgressBar.Value = 100;
            ProgressPercentText.Text = "100%";
            UpdateStages(100, !result.Succeeded);
            _statusKey = result.State switch
            {
                "success" => "Updater.Status.SuccessDetail",
                "rolled_back" => "Updater.Status.RolledBack",
                _ => "Updater.Status.Failed",
            };
            _statusArgs = result.State == "success" || result.State == "rolled_back"
                ? Array.Empty<object?>()
                : new object?[] { result.Message };
            StatusText.Text = _statusArgs.Length == 0
                ? Loc[_statusKey]
                : Loc.Format(_statusKey, _statusArgs);

            StatusText.Foreground = result.Succeeded
                ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
                : (System.Windows.Media.Brush)FindResource("WarningBrush");
            StatusDetailText.Text = result.Message;

            if (result.Succeeded)
            {
                SetFooterHint("Updater.Detail.Completed");
            }
            else
            {
                SetFooterHint("Updater.Detail.NotCompleted");
            }
        }
        catch (Exception ex)
        {
            _finished = true;
            CloseButton.IsEnabled = true;
            SetStatus("Updater.Status.Exception", ex.Message);
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            StatusDetailText.Text = ex.Message;
            LogExpander.IsExpanded = true;
            UpdateStages(100, true);
        }
    }

    private void UpdateStages(double percent, bool isError)
    {
        var activeIndex = percent switch
        {
            < 20 => 1,
            < 35 => 2,
            < 50 => 3,
            < 85 => 4,
            _ => 5,
        };

        var stages = new[]
        {
            (Stage1Circle, Stage1Number),
            (Stage2Circle, Stage2Number),
            (Stage3Circle, Stage3Number),
            (Stage4Circle, Stage4Number),
            (Stage5Circle, Stage5Number),
        };

        for (var index = 0; index < stages.Length; index++)
        {
            var stageNumber = index + 1;
            var state = stageNumber < activeIndex
                ? StageState.Done
                : stageNumber == activeIndex
                    ? isError ? StageState.Error : percent >= 100 ? StageState.Done : StageState.Active
                    : StageState.Pending;
            ApplyStageState(stages[index].Item1, stages[index].Item2, state);
        }
    }

    private void ApplyStageState(Border circle, TextBlock number, StageState state)
    {
        switch (state)
        {
            case StageState.Done:
                circle.Background = (System.Windows.Media.Brush)FindResource("SuccessSoftBrush");
                number.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
                number.Text = "✓";
                break;
            case StageState.Active:
                circle.Background = (System.Windows.Media.Brush)FindResource("AccentSoftBrush");
                number.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
                break;
            case StageState.Error:
                circle.Background = (System.Windows.Media.Brush)FindResource("DangerSoftBrush");
                number.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
                number.Text = "!";
                break;
            default:
                circle.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(237, 241, 247));
                number.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
                break;
        }
    }

    private enum StageState
    {
        Pending,
        Active,
        Done,
        Error,
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <inheritdoc />
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_finished)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
