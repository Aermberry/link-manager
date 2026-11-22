using System.IO;
using LinkManager.Core.Migration;
using LinkManager.Core.Registry;

namespace LinkManager.Gui;

/// <summary>向导五步：选择源目录 → 选择目标位置 → 预览(dry-run 计划) → 确认执行 → 结果摘要。</summary>
public enum WizardStep
{
    Source,
    Target,
    Preview,
    Execute,
    Result,
}

/// <summary>
/// 迁移向导状态机（无 WPF 依赖，可测）。计划编制、dry-run、执行、登记全部走
/// Core（<see cref="MigrationPlanner"/> / <see cref="MigrationRunner"/>），
/// 这里只管步骤推进、中文阻碍展示与结果数据转发——不复制业务逻辑。
/// 进程探测经 <paramref name="probe"/> 统一注入：预览与执行用同一探测基准，
/// 生产用 <see cref="DefaultProcessProbe"/>（真扫描），测试注入固定结果。
/// </summary>
public sealed class MigrationWizardModel
{
    private readonly string? _registryRoot;
    private readonly IProcessProbe? _probe;
    private MigrationPlan? _plan;
    private MigrationResult? _result;

    /// <summary>测试接缝：替换执行委托（真实实现即 <see cref="MigrationRunner.Run"/>）。</summary>
    internal Func<MigrationPlan, MigrationResult> ExecuteHandler { get; set; }

    public MigrationWizardModel(string? registryRoot = null, IProcessProbe? probe = null)
    {
        _registryRoot = registryRoot;
        _probe = probe;
        // 默认委托 Core 真执行；探测基准与预览一致（生产 = 真扫描，测试 = 注入）
        ExecuteHandler = plan => new MigrationRunner(
            probeFactory: _ => _probe ?? new DefaultProcessProbe()).Run(plan);
    }

    public WizardStep CurrentStep { get; private set; } = WizardStep.Source;

    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string Note { get; set; } = "";

    /// <summary>预览步展示的 Core dry-run 报告（进入预览步时刷新）。</summary>
    public string PreviewText { get; private set; } = "";

    /// <summary>前置检查的中文阻碍项（空 = 可执行）。来源：Core 只读护栏检查。</summary>
    public IReadOnlyList<string> Blockers { get; private set; } = [];

    public bool IsExecutable => Blockers.Count == 0;

    /// <summary>执行结果（结果步展示：Ok/报告/文件数/字节数）。</summary>
    public MigrationResult? Result => _result;

    /// <summary>登记表中新增的条目（结果步展示链接路径；null = 未能确认）。</summary>
    public LinkEntry? RegistryEntry { get; private set; }

    /// <summary>执行失败的中文原因（回到预览步展示）。</summary>
    public string ErrorMessage { get; private set; } = "";

    public bool CanGoNext => CurrentStep switch
    {
        WizardStep.Source => !string.IsNullOrWhiteSpace(SourcePath),
        WizardStep.Target => !string.IsNullOrWhiteSpace(TargetPath),
        _ => false, // Preview→Execute 需显式确认；Result 无下一步
    };

    /// <summary>进入下一步；所需输入缺失时抛出（调用方先看 <see cref="CanGoNext"/>）。</summary>
    public void GoNext()
    {
        switch (CurrentStep)
        {
            case WizardStep.Source:
                RequireInput(SourcePath, "请选择要迁移的源目录");
                CurrentStep = WizardStep.Target;
                break;

            case WizardStep.Target:
                RequireInput(TargetPath, "请选择目标位置");
                CurrentStep = WizardStep.Preview;
                BuildPreview();
                break;

            default:
                throw new InvalidOperationException($"当前步骤（{CurrentStep}）没有下一步");
        }
    }

    public void GoBack()
    {
        CurrentStep = CurrentStep switch
        {
            WizardStep.Target => WizardStep.Source,
            WizardStep.Preview => WizardStep.Target,
            WizardStep.Result => WizardStep.Preview,
            _ => CurrentStep,
        };
    }

    /// <summary>
    /// 确认执行：仅在预览步、无阻碍时可用。走 Core 真执行；
    /// 成功 → 结果步；失败 → 中文原因留在预览步（源数据由 Core 保持原样）。
    /// </summary>
    public void ConfirmExecute()
    {
        if (CurrentStep != WizardStep.Preview)
        {
            throw new InvalidOperationException("只有预览步可以确认执行");
        }
        if (!IsExecutable)
        {
            throw new InvalidOperationException(
                "存在未解决的前置问题，不能执行：" + string.Join("；", Blockers));
        }

        try
        {
            _result = ExecuteHandler(_plan!);
        }
        catch (MigrationException ex)
        {
            ErrorMessage = ex.Message;
            return; // 留在预览步，错误原因展示给用户
        }
        catch (Exception ex) when (ex is RegistryCorruptException or RegistryIoException
                                        or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"迁移失败：{ex.Message}";
            return;
        }

        if (_result.Ok)
        {
            ErrorMessage = "";
            ReadRegistryEntry();
            CurrentStep = WizardStep.Result;
        }
        else
        {
            ErrorMessage = _result.Report;
        }
    }

    /// <summary>
    /// 测试专用：替换执行委托后确认执行。配合「预览可执行」的状态使用，
    /// 等效模拟预览之后执行瞬间环境突变（进程被拉起等）的竞态路径。
    /// </summary>
    internal void ConfirmExecuteForTest(Func<MigrationPlan, MigrationResult> handler)
    {
        ExecuteHandler = handler;
        ConfirmExecute();
    }

    /// <summary>编制计划并刷新预览/阻碍项（每次进入预览步都重新探测，不缓存旧状态）。</summary>
    private void BuildPreview()
    {
        _plan = MigrationPlanner.Plan(SourcePath.Trim(), TargetPath.Trim(), note: Note.Trim(),
            registryRoot: _registryRoot, probe: _probe ?? new DefaultProcessProbe());

        var dryRun = new MigrationRunner().DryRun(_plan);
        PreviewText = dryRun.Report;
        // Core 只读护栏：源/目标形态 + 计划内探测到的运行中进程，统一中文输出
        Blockers = MigrationRunner.FindBlockers(_plan);
    }

    /// <summary>从登记表读回新条目（结果步展示「列表出现新链接」）。</summary>
    private void ReadRegistryEntry()
    {
        try
        {
            var registry = LinkRegistry.Load(_registryRoot);
            RegistryEntry = registry.Entries
                .FirstOrDefault(e => string.Equals(
                    e.Source, _plan!.Source, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is RegistryCorruptException or RegistryIoException)
        {
            RegistryEntry = null; // 登记读取失败不掩盖迁移成功本身
        }
    }

    private static void RequireInput(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }
    }
}
