using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LinkManager.Core.Migration;
using LinkManager.Core.Registry;

namespace LinkManager.Gui;

/// <summary>
/// 五步迁移向导窗口：UI 只做步骤呈现与目录选择，全部业务走
/// <see cref="MigrationWizardModel"/>（其内只调 Core）。
/// 顶部 1-5 步进度指示条高亮当前步；预览/执行两步为危险步骤（删源、回指），
/// 红色警示强化。前置检查失败（进程占用、目标非空等）在预览步以中文原因展示
/// 并阻断执行；执行瞬间失败（竞态）回预览步以红色中文原因展示。
/// </summary>
public partial class MigrationWizardWindow : Window
{
    private readonly MigrationWizardModel _model;

    public MigrationWizardWindow()
    {
        InitializeComponent();
        _model = new MigrationWizardModel();
        ShowStep();
    }

    private void ShowStep()
    {
        SourcePanel.Visibility = _model.CurrentStep == WizardStep.Source ? Visibility.Visible : Visibility.Collapsed;
        TargetPanel.Visibility = _model.CurrentStep == WizardStep.Target ? Visibility.Visible : Visibility.Collapsed;
        PreviewPanel.Visibility = _model.CurrentStep == WizardStep.Preview ? Visibility.Visible : Visibility.Collapsed;
        ExecutePanel.Visibility = _model.CurrentStep == WizardStep.Execute ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = _model.CurrentStep == WizardStep.Result ? Visibility.Visible : Visibility.Collapsed;

        RenderStepIndicator();

        StepTitle.Text = _model.CurrentStep switch
        {
            WizardStep.Source => "迁移向导 · 第 1 步 / 共 5 步：选择源目录",
            WizardStep.Target => "迁移向导 · 第 2 步 / 共 5 步：选择目标位置",
            WizardStep.Preview => "迁移向导 · 第 3 步 / 共 5 步：预览迁移计划",
            WizardStep.Execute => "迁移向导 · 第 4 步 / 共 5 步：正在执行",
            WizardStep.Result => "迁移向导 · 第 5 步 / 共 5 步：结果摘要",
            _ => "迁移向导",
        };

        if (_model.CurrentStep == WizardStep.Preview)
        {
            PreviewBox.Text = _model.PreviewText;
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(_model.ErrorMessage))
            {
                lines.Add("✗ " + _model.ErrorMessage);
            }
            lines.AddRange(_model.Blockers.Select(b => "· " + b));
            if (_model.IsExecutable)
            {
                BlockersText.Text = string.IsNullOrEmpty(_model.ErrorMessage)
                    ? "✓ 前置检查全部通过，可以执行迁移。"
                    : "";
                BlockersText.Foreground = FindResource("AccentBrush") as Brush ?? Brushes.Green;
            }
            else
            {
                BlockersText.Text = "无法执行，原因：\n" + string.Join("\n", lines);
                BlockersText.Foreground = FindResource("DangerBrush") as Brush ?? Brushes.Firebrick;
            }
        }

        if (_model.CurrentStep == WizardStep.Result && _model.Result is { } result)
        {
            ResultBox.Text = BuildSummary(result);
            RenderResultBanner(result.Ok);
        }

        BackButton.IsEnabled = _model.CurrentStep is WizardStep.Target or WizardStep.Preview;
        NextButton.IsEnabled = _model.CurrentStep switch
        {
            WizardStep.Source or WizardStep.Target => true,
            WizardStep.Preview => _model.IsExecutable, // 有阻碍时按钮真实禁用
            WizardStep.Result => true,
            _ => false, // Execute 期间不允许再点
        };
        NextButton.Content = _model.CurrentStep switch
        {
            WizardStep.Preview => "确认执行",
            WizardStep.Result => "完成",
            _ => "下一步",
        };
    }

    /// <summary>
    /// 渲染顶部步骤指示条：每步一个圆点数字 + 名称，当前步强调、已完成步淡色、
    /// 未到步灰显；第 3/4 步（预览/执行，含删源与回指）为危险步，红点标记。
    /// </summary>
    private void RenderStepIndicator()
    {
        var accent = (Brush)FindResource("AccentBrush");
        var danger = (Brush)FindResource("DangerBrush");
        var subtle = (Brush)FindResource("SubtleTextBrush");
        var border = (Brush)FindResource("CardBorderBrush");
        var labels = new[] { "选源目录", "选目标", "预览计划", "执行迁移", "结果" };
        var dangerSteps = new HashSet<WizardStep> { WizardStep.Preview, WizardStep.Execute };
        var current = (int)_model.CurrentStep;

        StepIndicatorPanel.Children.Clear();
        for (var i = 0; i < labels.Length; i++)
        {
            var step = (WizardStep)i;
            var isCurrent = i == current;
            var isDone = i < current;
            var isDanger = dangerSteps.Contains(step);

            var circle = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                VerticalAlignment = VerticalAlignment.Center,
                Background = isCurrent ? (isDanger ? danger : accent)
                    : isDone ? (isDanger ? danger : accent)
                    : Brushes.White,
                BorderBrush = isCurrent || isDone ? (isDanger ? danger : accent) : border,
                BorderThickness = new Thickness(1),
            };
            var number = new TextBlock
            {
                Text = (i + 1).ToString(),
                Foreground = isCurrent || isDone ? Brushes.White : subtle,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            circle.Child = number;

            var label = new TextBlock
            {
                Text = labels[i],
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12.5,
                FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isCurrent ? (isDanger ? danger : accent) : isDone ? subtle : subtle,
            };

            StepIndicatorPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(i == 0 ? 0 : 14, 0, 0, 0),
                Children = { circle, label },
            });

            if (i < labels.Length - 1)
            {
                StepIndicatorPanel.Children.Add(new TextBlock
                {
                    Text = "—",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    Foreground = border,
                });
            }
        }
    }

    /// <summary>
    /// 结果步横幅：成功绿（✓ 迁移完成，原位置已是链接）/ 失败红（✗ 迁移未完成）。
    /// 失败时正文明细已在预览步以红色原因展示，这里只给结论行。
    /// </summary>
    private void RenderResultBanner(bool ok)
    {
        var okBrush = FindResource("AccentBrush") as Brush ?? Brushes.RoyalBlue;
        var okBg = FindResource("DangerBackgroundBrush") as Brush;
        // 成功横幅用 Fluent 绿系（与状态列 Ok 一致）；失败沿用危险红系资源
        var green = new SolidColorBrush(Color.FromRgb(0x0F, 0x7B, 0x0F));
        var greenBg = new SolidColorBrush(Color.FromRgb(0xF3, 0xFA, 0xF3));
        var greenBorder = new SolidColorBrush(Color.FromRgb(0xC5, 0xE1, 0xC5));

        if (ok)
        {
            ResultBannerGlyph.Text = "✓";
            ResultBannerGlyph.Foreground = green;
            ResultBannerText.Text = "迁移完成：数据已搬到目标位置，原位置已改写为链接（可在主窗口登记列表查看）。";
            ResultBannerText.Foreground = green;
            ResultBanner.Background = greenBg;
            ResultBanner.BorderBrush = greenBorder;
        }
        else
        {
            ResultBannerGlyph.Text = "✗";
            ResultBannerGlyph.Foreground = FindResource("DangerBrush") as Brush ?? Brushes.Firebrick;
            ResultBannerText.Text = "迁移未完成：原因见上方红色说明（返回预览步可重试）。";
            ResultBannerText.Foreground = FindResource("DangerBrush") as Brush ?? Brushes.Firebrick;
            ResultBanner.Background = FindResource("DangerBackgroundBrush") as Brush ?? okBg ?? Brushes.White;
            ResultBanner.BorderBrush = FindResource("DangerBorderBrush") as Brush ?? okBrush;
        }
    }

    private string BuildSummary(MigrationResult result)
    {
        var lines = new List<string> { result.Report };

        if (result.Stats is { } stats)
        {
            lines.Add($"文件数：{stats.FileCount}");
            lines.Add($"总字节数：{stats.TotalBytes:N0}（约 {stats.TotalBytes / 1024.0 / 1024.0:F1} MB）");
        }

        if (_model.Elapsed is { } elapsed)
        {
            lines.Add($"耗时：{elapsed.TotalSeconds:F1} 秒");
        }

        if (_model.RegistryEntry is { } entry)
        {
            lines.Add($"链接路径（原位置）：{entry.Source}");
            lines.Add($"数据实际位置：{entry.Target}");
            lines.Add("该链接已出现在主窗口登记列表中。");
        }

        if (_model.RegistryReadError is { } registryError)
        {
            lines.Add($"⚠ {registryError}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择要迁移的文件夹（源目录）",
        };
        if (dialog.ShowDialog(this) == true)
        {
            SourceInput.Text = dialog.FolderName;
        }
    }

    private void BrowseTargetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择目标位置（目标文件夹需为空或尚未创建）",
        };
        if (dialog.ShowDialog(this) == true)
        {
            TargetInput.Text = dialog.FolderName;
        }
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            switch (_model.CurrentStep)
            {
                case WizardStep.Source:
                    _model.SourcePath = SourceInput.Text;
                    _model.Note = NoteInput.Text;
                    _model.GoNext();
                    break;

                case WizardStep.Target:
                    _model.TargetPath = TargetInput.Text;
                    _model.GoNext();
                    break;

                case WizardStep.Preview when _model.IsExecutable:
                    await ExecuteMigrationAsync();
                    return;

                case WizardStep.Result:
                    DialogResult = true;
                    return;

                default:
                    return;
            }
        }
        catch (InvalidOperationException ex)
        {
            DialogMessage.Show(this, DialogMessage.Kind.Warning, "无法继续", ex.Message);
            return;
        }
        ShowStep();
    }

    private async Task ExecuteMigrationAsync()
    {
        _model.EnterExecute();
        ShowStep();
        ExecuteDetail.Text = "正在复制与核对，完成后会自动继续…";

        // Core 执行是同步 IO/进程调用；丢到线程池跑，避免不确定进度条在 robocopy 期间冻结
        await Task.Run(_model.CompleteExecute);
        ShowStep();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _model.GoBack();
        ShowStep();
    }

    /// <summary>冒烟探针入口：以编程方式推进向导（仅测试工程/探针使用）。</summary>
    internal void RaiseNext() => NextButton_Click(this, new RoutedEventArgs());
}
