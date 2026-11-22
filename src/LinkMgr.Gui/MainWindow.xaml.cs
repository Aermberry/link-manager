using System.IO;
using System.Windows;
using System.Windows.Media;
using LinkMgr.Core.Health;
using LinkMgr.Core.Registry;

namespace LinkMgr.Gui;

/// <summary>列表行视图模型：登记条目 + 健康状态。</summary>
public sealed class EntryRow
{
    public required LinkEntry Entry { get; init; }
    public required LinkStatus Status { get; init; }

    public string Source => Entry.Source;
    public string Target => Entry.Target;
    public string Type => Entry.Type;
    public string Note => Entry.Note;

    public string StatusText => Status switch
    {
        LinkStatus.Ok => "正常",
        LinkStatus.Broken => "断链",
        LinkStatus.Missing => "链接缺失",
        LinkStatus.Disguised => "伪装目录",
        _ => "未知",
    };

    public Brush StatusBrush => Status switch
    {
        LinkStatus.Ok => Brushes.Green,
        LinkStatus.Broken => Brushes.Red,
        LinkStatus.Missing => Brushes.Orange,
        LinkStatus.Disguised => Brushes.Gray,
        _ => Brushes.Black,
    };
}

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadRegistry();
        RefreshButton.Click += (_, _) => LoadRegistry();
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
