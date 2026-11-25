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
/// 逐条查重，已登记的不会被重复导入）。
/// </summary>
public partial class ScanDialog : Window
{
    /// <summary>本次实际新登记的条数（供主窗口刷新状态栏）。</summary>
    public int ImportedCount { get; private set; }

    private LinkRegistry? _registry;

    public ScanDialog()
    {
        InitializeComponent();
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
        var root = RootInput.Text.Trim();
        if (root.Length == 0 || !Directory.Exists(root))
        {
            MessageBox.Show(this, $"扫描目录不存在：{root}", "无法扫描",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int? maxDepth = null;
        var depthText = DepthInput.Text.Trim();
        if (depthText.Length > 0)
        {
            if (!int.TryParse(depthText, out var depth) || depth < 0)
            {
                MessageBox.Show(this, "深度需为非负整数：0 = 只扫这一层，1 = 连一层子目录；留空 = 不限深度。",
                    "深度无效", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            maxDepth = depth;
        }

        try
        {
            _registry = LinkRegistry.Load();
            var result = LinkScanner.Scan(root, _registry, maxDepth: maxDepth ?? -1);
            FoundList.ItemsSource = result.Links
                .Select(l => new ScannedLinkRow { Link = l })
                .ToList();

            var skippedNote = result.Skipped.Count > 0 ? $"，{result.Skipped.Count} 个目录无权限跳过" : "";
            SummaryText.Text = result.Links.Count > 0
                ? $"发现 {result.Links.Count} 条待登记链接（已登记 {result.RegisteredCount} 条不再列出，共扫过 {result.ScannedDirectoryCount} 个目录{skippedNote}）。勾选后点「登记所选」。"
                : $"未发现待登记的链接（已登记 {result.RegisteredCount} 条，共扫过 {result.ScannedDirectoryCount} 个目录{skippedNote}）。";
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
            MessageBox.Show(this, $"扫描失败：{ex.Message}", "无法扫描",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
            MessageBox.Show(this, "请先勾选要登记的链接。", "未选择",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
            SummaryText.Text = imported == selected.Count
                ? $"已登记 {imported} 条。"
                : $"已登记 {imported} 条（其余已在登记表中，未重复登记）。";
            // 已登记的从候选列表移除；剩下的保持可见、取消勾选，供再次尝试
            var remaining = ((List<ScannedLinkRow>)FoundList.ItemsSource!)
                .Where(r => !_registry.ContainsEntry(r.Link.Source))
                .ToList();
            foreach (var row in remaining)
            {
                row.IsSelected = false;
            }
            FoundList.ItemsSource = remaining;
            UpdateImportButton();
        }
        catch (RegistryCorruptException ex)
        {
            ShowRegistryError("登记文件损坏", ex.Message);
        }
        catch (RegistryIoException ex)
        {
            ShowRegistryError("登记文件读写失败", ex.Message);
        }
    }

    private void UpdateImportButton()
    {
        var hasRegistry = _registry is not null;
        var hasRows = FoundList.ItemsSource is List<ScannedLinkRow> rows && rows.Count > 0;
        ImportButton.IsEnabled = hasRegistry && hasRows;
    }

    private void ShowRegistryError(string title, string message)
        => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
