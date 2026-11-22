using System.IO;
using System.Windows;
using LinkManager.Core.Migration;
using LinkManager.Core.Registry;

namespace LinkManager.Gui;

/// <summary>
/// 五步迁移向导窗口：UI 只做步骤呈现与文件对话框，全部业务走
/// <see cref="MigrationWizardModel"/>（其内只调 Core）。
/// 前置检查失败（进程占用、目标非空等）在预览步以中文原因展示并阻断执行。
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

        StepTitle.Text = _model.CurrentStep switch
        {
            WizardStep.Source => "迁移向导 · 第 1 步 / 共 4 步：选择源目录",
            WizardStep.Target => "迁移向导 · 第 2 步 / 共 4 步：选择目标位置",
            WizardStep.Preview => "迁移向导 · 第 3 步 / 共 4 步：预览迁移计划",
            WizardStep.Execute => "迁移向导 · 第 4 步 / 共 4 步：正在执行",
            WizardStep.Result => "迁移向导 · 完成：结果摘要",
            _ => "迁移向导",
        };

        if (_model.CurrentStep == WizardStep.Preview)
        {
            PreviewBox.Text = _model.PreviewText;
            BlockersText.Text = _model.IsExecutable
                ? "✓ 前置检查全部通过，可以执行迁移。"
                : "无法执行，原因：\n" + string.Join("\n", _model.Blockers.Select(b => "· " + b));
        }

        if (_model.CurrentStep == WizardStep.Result && _model.Result is { } result)
        {
            ResultBox.Text = BuildSummary(result);
        }

        BackButton.IsEnabled = _model.CurrentStep is WizardStep.Target or WizardStep.Preview;
        NextButton.IsEnabled = _model.CurrentStep != WizardStep.Execute;
        NextButton.Content = _model.CurrentStep switch
        {
            WizardStep.Preview => _model.IsExecutable ? "确认执行" : "无法执行",
            WizardStep.Result => "完成",
            _ => "下一步",
        };
    }

    private string BuildSummary(MigrationResult result)
    {
        var lines = new List<string> { result.Report };

        if (result.Stats is { } stats)
        {
            lines.Add($"文件数：{stats.FileCount}");
            lines.Add($"总字节数：{stats.TotalBytes:N0}（约 {stats.TotalBytes / 1024.0 / 1024.0:F1} MB）");
        }

        if (_model.RegistryEntry is { } entry)
        {
            lines.Add($"链接路径（原位置）：{entry.Source}");
            lines.Add($"数据实际位置：{entry.Target}");
            lines.Add("该链接已出现在主窗口登记列表中。");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        // 同样借用文件对话框选目录：任选源文件夹里的一个文件，取其所在目录
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要迁移的文件夹：进入该文件夹后任选其中一个文件",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            SourceInput.Text = Path.GetDirectoryName(dialog.FileName) ?? "";
        }
    }

    private void BrowseTargetButton_Click(object sender, RoutedEventArgs e)
    {
        // WPF 无原生目录对话框：借用"另存为"选目录——进入目标文件夹后随便点"保存"即可
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "选择目标文件夹：进入该文件夹后点“保存”",
            FileName = "将迁移到这里",
        };
        if (dialog.ShowDialog(this) == true)
        {
            TargetInput.Text = Path.GetDirectoryName(dialog.FileName) ?? "";
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
            MessageBox.Show(this, ex.Message, "无法继续", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ShowStep();
    }

    private async Task ExecuteMigrationAsync()
    {
        // 执行中视图：模型仍停在 Preview 步（ConfirmExecute 从那里发起并推进到 Result）
        ExecutePanel.Visibility = Visibility.Visible;
        PreviewPanel.Visibility = Visibility.Collapsed;
        StepTitle.Text = "迁移向导 · 第 4 步 / 共 4 步：正在执行";
        BackButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        ExecuteDetail.Text = "正在复制与核对，完成后会自动继续…";
        await Task.Delay(50); // 让执行中界面先渲染

        _model.ConfirmExecute();
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
