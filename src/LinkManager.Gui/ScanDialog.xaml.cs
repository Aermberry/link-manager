using System.IO;
using System.Windows;
using LinkManager.Core.Registry;
using LinkManager.Core.Scan;

namespace LinkManager.Gui;

/// <summary>扫描结果行视图模型（勾选批量登记）。</summary>
public sealed class ScannedLinkRow
{
    public required ScannedLink Link { get; init; }

    public string Source => Link.Source;
    public string Target => Link.Target;
    public string Type => Link.Type;

    public bool IsSelected { get; set; } = true;
}

/// <summary>
/// 扫描存量链接对话框（T5 scan API 的 GUI 窗口）：选目录、深度可选、
/// 列表展示发现的链接、勾选批量登记。只调 Core：
/// <see cref="LinkScanner.Scan"/>（只读发现）+ <see cref="LinkImporter"/>（落登记表，
/// 逐条查重，已登记的不会被重复导入）。校验/汇总/空状态文案在
/// <see cref="ScanDialogLogic"/>（可单测）。
/// </summary>
public partial class ScanDialog : Window
{
    /// <summary>本次实际新登记的条数（供主窗口刷新状态栏）。</summary>
    public int ImportedCount { get; private set; }

    private LinkRegistry? _registry;
    private bool _hasScannedThisSession;

    public ScanDialog()
    {
        InitializeComponent();
        ApplyEmptyState();
        UpdateImportButton();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择要扫描的目录" };
        if (dialog.ShowDialog(this) == true)
        {
            RootInput.Text = dialog.FolderName;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var rootError = ScanDialogLogic.ValidateRoot(RootInput.Text);
        if (rootError is not null)
        {
            DialogMessage.Show(this, DialogMessage.Kind.Info, "无法扫描", rootError);
            return;
        }

        var root = RootInput.Text.Trim();
        var depth = ScanDialogLogic.ParseDepth(DepthInput.Text, out var depthError);
        if (depthError is not null)
        {
            DialogMessage.Show(this, DialogMessage.Kind.Info, "深度无效", depthError);
            return;
        }

        try
        {
            _registry = LinkRegistry.Load();
            var result = LinkScanner.Scan(root, _registry, maxDepth: depth ?? -1);
            _hasScannedThisSession = true;
            FoundList.ItemsSource = result.Links
                .Select(l => new ScannedLinkRow { Link = l })
                .ToList();
            SummaryText.Text = ScanDialogLogic.SummaryLine(
                result.Links.Count, result.RegisteredCount, result.ScannedDirectoryCount,
                result.Skipped.Count, hasImportedThisSession: false);
        }
        catch (RegistryCorruptException ex)
        {
            ShowRegistryError("登记文件损坏", ex.Message);
        }
        catch (RegistryIoException ex)
        {
            ShowRegistryError("登记文件读写失败", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DialogMessage.Show(this, DialogMessage.Kind.Warning, "无法扫描", $"扫描失败：{ex.Message}");
        }
        ApplyEmptyState();
        UpdateImportButton();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_registry is null)
        {
            return;
        }

        var selected = ((List<ScannedLinkRow>)FoundList.ItemsSource!).Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            DialogMessage.Show(this, DialogMessage.Kind.Info, "未选择", "请先勾选要登记的链接。");
            return;
        }

        try
        {
            var imported = 0;
            foreach (var row in selected)
            {
                // ImportOne 逐条查重：扫描后登记表可能变化，不产生重复条目
                if (LinkImporter.ImportOne(row.Link, _registry))
                {
                    imported++;
                }
            }

            ImportedCount = imported;
            SummaryText.Text = ScanDialogLogic.ImportedLine(imported, selected.Count);
            // 已登记的从候选列表移除；剩下的保持可见、取消勾选，供再次尝试
            var remaining = ((List<ScannedLinkRow>)FoundList.ItemsSource!)
                .Where(r => !_registry.ContainsEntry(r.Link.Source))
                .ToList();
            foreach (var row in remaining)
            {
                row.IsSelected = false;
            }
            FoundList.ItemsSource = remaining;
        }
        catch (RegistryCorruptException ex)
        {
            ShowRegistryError("登记文件损坏", ex.Message);
        }
        catch (RegistryIoException ex)
        {
            ShowRegistryError("登记文件读写失败", ex.Message);
        }
        ApplyEmptyState();
        UpdateImportButton();
    }

    /// <summary>空状态覆盖层：有候选行时隐藏；无结果时按「未扫描 / 扫过无结果」显示对应文案。</summary>
    private void ApplyEmptyState()
    {
        var rowCount = FoundList.ItemsSource is List<ScannedLinkRow> rows ? rows.Count : 0;
        var empty = ScanDialogLogic.EmptyState(_hasScannedThisSession, rowCount);
        if (empty is null)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            FoundList.Visibility = Visibility.Visible;
            return;
        }

        (EmptyTitle.Text, EmptyDetail.Text) = empty.Value;
        EmptyState.Visibility = Visibility.Visible;
        FoundList.Visibility = Visibility.Collapsed;
    }

    private void UpdateImportButton()
    {
        var rowCount = FoundList.ItemsSource is List<ScannedLinkRow> rows ? rows.Count : 0;
        ImportButton.IsEnabled = ScanDialogLogic.ImportEnabled(_hasScannedThisSession, rowCount);
    }

    private void ShowRegistryError(string title, string message)
        => DialogMessage.Show(this, DialogMessage.Kind.Warning, title, message);
}
