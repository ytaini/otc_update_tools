using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Otc.Updater;

/// <summary>更新进度窗口。</summary>
public partial class MainWindow : Window
{
    private readonly UpdateLaunchOptions? _options;
    private readonly StringBuilder _log = new();
    private bool _finished;

    /// <summary>初始化更新窗口。</summary>
    public MainWindow(UpdateLaunchOptions? options, string? startupError)
    {
        InitializeComponent();
        _options = options;

        if (options is null)
        {
            UpdateContent.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            FooterHintText.Text = startupError ?? "请由主程序启动更新器。";
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
            StatusText.Text = result.State switch
            {
                "success" => "更新成功，新版本已通过健康检查。",
                "rolled_back" => "更新失败，已自动恢复到旧版本。",
                _ => $"更新失败：{result.Message}",
            };
            StatusText.Foreground = result.Succeeded
                ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
                : (System.Windows.Media.Brush)FindResource("WarningBrush");
            StatusDetailText.Text = result.Message;
            FooterHintText.Text = result.Succeeded
                ? "更新已完成，可以关闭此窗口。"
                : "更新未完成，系统已尝试恢复旧版本。";
        }
        catch (Exception ex)
        {
            _finished = true;
            CloseButton.IsEnabled = true;
            StatusText.Text = $"更新器异常：{ex.Message}";
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
