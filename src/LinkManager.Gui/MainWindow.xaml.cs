using System.IO;
using System.Windows;
using System.Windows.Media;
using LinkManager.Core.Health;
using LinkManager.Core.Links;
using LinkManager.Core.Registry;

namespace LinkManager.Gui;

/// <summary>列表行视图模型：登记条目 + 健康状态。</summary>
public sealed class EntryRow
{
    public required LinkEntry Entry { get; init; }
    public required LinkStatus Status { get; init; }

    public string Source => Entry.Source;
    public string Target => Entry.Target;
    public string Type => Entry.Type;
    public string Note => Entry.Note;

    public string StatusText => Status.ToChinese();

    public Brush StatusBrush
    {
        get
        {
            var (r, g, b) = Status.ToRgb();
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
        RefreshButton.Click += (_, _) => LoadRegistry();
    }

    /// <summary>迁移向导入口：完成后刷新登记列表，让新链接立即可见。</summary>
    private void WizardButton_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new MigrationWizardWindow { Owner = this };
        wizard.ShowDialog();
        LoadRegistry();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var source = SourcePathInput.Text.Trim();
        var target = TargetPathInput.Text.Trim();
        var note = NoteInput.Text.Trim();

        try
        {
            var entry = LinkCreator.CreateJunction(source, target, note);
            SourcePathInput.Text = "";
            TargetPathInput.Text = "";
            NoteInput.Text = "";
            LoadRegistry();
            StatusText.Text = LinkCreator.DescribeCreated(entry);
        }
        catch (LinkCreateException ex)
        {
            MessageBox.Show(this, ex.Message, "创建失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(this, ex.Message, "创建失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadRegistry()
    {
        try
        {
            var registry = LinkRegistry.Load();
            EntriesList.ItemsSource = registry.Entries
                .Select(e => new EntryRow { Entry = e, Status = LinkHealth.Check(e.Source, e.Type) })
                .ToList();
            var broken = ((List<EntryRow>)EntriesList.ItemsSource).Count(r => r.Status != LinkStatus.Ok);
            StatusText.Text = broken > 0
                ? $"{registry.Entries.Count} 条登记，{broken} 条异常 · {registry.FilePath}"
                : $"{registry.Entries.Count} 条登记，全部正常 · {registry.FilePath}";
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
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
