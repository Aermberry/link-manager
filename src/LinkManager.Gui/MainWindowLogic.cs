namespace LinkManager.Gui;

/// <summary>主窗口状态栏文案与可测试的行操作判断（纯函数）。</summary>
public static class MainWindowLogic
{
    /// <summary>加载登记表后的状态栏一行：全部正常 / N 条异常 + 登记文件路径。</summary>
    public static string StatusLine(int count, int broken, string filePath) => broken > 0
        ? $"{count} 条登记，{broken} 条异常 · {filePath}"
        : $"{count} 条登记，全部正常 · {filePath}";
}
