using System.Windows;
using LinkManager.Core.Links;
using LinkManager.Core.Registry;

namespace LinkManager.Gui;

/// <summary>
/// 创建 junction 对话框：数据已在新位置时直接建链接并登记，不搬数据。
/// 表单语义（与 LinkCreator.CreateJunction 一致）：源 = 数据真实位置，
/// 目标 = 链接所在处。旧文案「链接处（源）与新位置（目标）」与实现语义相反
/// （照旧文案填必报「源路径不存在」），本对话框文案已按实现语义修正。
/// </summary>
public partial class CreateLinkDialog : Window
{
    /// <summary>创建成功后的登记条目（供主窗口刷新状态栏）；null = 未创建（取消/失败）。</summary>
    public LinkEntry? CreatedEntry { get; private set; }

    public CreateLinkDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => SourcePathInput.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var source = SourcePathInput.Text.Trim();
        var target = TargetPathInput.Text.Trim();
        var note = NoteInput.Text.Trim();

        if (source.Length == 0 || target.Length == 0)
        {
            MessageBox.Show(this, "请填写数据真实位置（源）与链接位置（目标）。",
                "缺少路径", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            CreatedEntry = LinkCreator.CreateJunction(source, target, note);
            DialogResult = true;
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

    private void ShowRegistryError(string title, string message)
        => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
