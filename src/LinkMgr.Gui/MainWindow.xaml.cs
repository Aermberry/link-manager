using System.IO;
using System.Windows;
using LinkMgr.Core.Registry;

namespace LinkMgr.Gui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadRegistry();
    }

    private void LoadRegistry()
    {
        try
        {
            var registry = LinkRegistry.Load();
            EntriesList.ItemsSource = registry.Entries;
            StatusText.Text = $"{registry.Entries.Count} 条登记 · {registry.FilePath}";
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
