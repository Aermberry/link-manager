using System.Windows;
using System.Windows.Media;
using LinkManager.Core.Health;
using LinkManager.Core.Links;
using LinkManager.Core.Registry;

namespace LinkManager.Gui;

/// <summary>列表行视图模型：登记条目 + 健康状态（视觉映射见 <see cref="EntryStatusVisual"/>）。</summary>
public sealed class EntryRow
{
    public required LinkEntry Entry { get; init; }
    public required LinkStatus Status { get; init; }

    public string Source => Entry.Source;
    public string Target => Entry.Target;
    public string Type => Entry.Type;
    public string Note => Entry.Note;

    public string StatusText => Status.ToChinese();

    public string StatusGlyph => EntryStatusVisual.Glyph(Status);

    public Brush StatusBrush
    {
        get
        {
            var (r, g, b) = EntryStatusVisual.Color(Status);
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }
    }
}

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadRegistry();
    }

    /// <summary>迁移向导入口（默认强调入口）：完成后刷新登记列表，让新链接立即可见。</summary>
    private void WizardButton_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new MigrationWizardWindow { Owner = this };
        wizard.ShowDialog();
        LoadRegistry();
    }

    /// <summary>创建入口：原内嵌表单收进对话框（创建 = 只建链接不搬数据；搬家走迁移向导）。</summary>
    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateLinkDialog { Owner = this };
        dialog.ShowDialog();
        if (dialog.CreatedEntry is { } created)
        {
            LoadRegistry();
            StatusText.Text = LinkCreator.DescribeCreated(created);
        }
    }

    /// <summary>扫描入口：发现存量链接并勾选批量登记（T5 scan API 的 GUI 窗口）。</summary>
    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ScanDialog { Owner = this };
        dialog.ShowDialog();
        if (dialog.ImportedCount > 0)
        {
            LoadRegistry();
            StatusText.Text = $"扫描登记完成：新登记 {dialog.ImportedCount} 条";
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadRegistry();

    private void LoadRegistry()
    {
        try
        {
            var registry = LinkRegistry.Load();
            var rows = registry.Entries
                .Select(e => new EntryRow { Entry = e, Status = LinkHealth.Check(e.Source, e.Type) })
                .ToList();
            EntriesList.ItemsSource = rows;
            var broken = rows.Count(r => r.Status != LinkStatus.Ok);
            StatusText.Text = MainWindowLogic.StatusLine(registry.Entries.Count, broken, registry.FilePath);
            EmptyState.Visibility = registry.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EntriesList.Visibility = registry.Entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (RegistryCorruptException ex)
        {
            ShowRegistryError("登记文件损坏", ex.Message);
        }
        catch (RegistryIoException ex)
        {
            ShowRegistryError("登记文件读写失败", ex.Message);
        }
        catch (Exception ex)
        {
            ShowRegistryError("加载失败", ex.Message);
        }
    }

    private void ShowRegistryError(string title, string message)
    {
        StatusText.Text = $"{title}：{message}";
        DialogMessage.Show(this, DialogMessage.Kind.Warning, title, message);
    }
}
