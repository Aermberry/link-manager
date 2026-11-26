using System.Windows;
using System.Windows.Media;

namespace LinkManager.Gui;

/// <summary>统一风格消息弹窗（Fluent 浅色卡片样式，替代系统默认 MessageBox）。</summary>
public partial class DialogMessage : Window
{
    /// <summary>消息种类：决定左侧色条与图标符号（错误红 / 警告橙 / 确认蓝 / 成功绿）。</summary>
    public enum Kind
    {
        Error,
        Warning,
        Info,
        Success,
    }

    public DialogMessage()
    {
        InitializeComponent();
    }

    /// <summary>模态展示一条消息。title 为窗口与卡片标题，detail 为正文。</summary>
    public static void Show(Window? owner, Kind kind, string title, string detail)
    {
        var dialog = new DialogMessage
        {
            Title = title,
            Owner = owner,
            MessageTitle = title,
            MessageDetail = detail,
            Accent = kind switch
            {
                Kind.Error => ((Color)ColorConverter.ConvertFromString("#C42B1C"), "✗", "#FDF3F2", "#F1C5C0"),
                Kind.Warning => ((Color)ColorConverter.ConvertFromString("#CA5010"), "⚠", "#FDF7F2", "#F5D9C3"),
                Kind.Info => ((Color)ColorConverter.ConvertFromString("#0067C0"), "ℹ", "#EFF6FF", "#BBD8F5"),
                _ => ((Color)ColorConverter.ConvertFromString("#0F7B0F"), "✓", "#F3FAF3", "#C5E1C5"),
            },
        };
        dialog.ShowDialog();
    }

    /// <summary>便捷重载：owner = 当前活动窗口。</summary>
    public static void Show(Kind kind, string title, string detail)
        => Show(Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive), kind, title, detail);

    public string MessageTitle
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public string MessageDetail
    {
        get => DetailText.Text;
        set => DetailText.Text = value;
    }

    /// <summary>（色值，图标符号，卡片背景，卡片边框）四元组。</summary>
    internal (Color bar, string glyph, string cardBg, string cardBorder)? Accent { get; set; }

    private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (Accent is not { } a)
        {
            return;
        }
        var bar = new SolidColorBrush(a.bar);
        BarRect.Fill = bar;
        GlyphText.Text = a.glyph;
        GlyphText.Foreground = bar;
        CardBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(a.cardBg));
        CardBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(a.cardBorder));
    }
}
