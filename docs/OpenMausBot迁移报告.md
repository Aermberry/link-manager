# OpenMausBot 数据迁移工作报告

- **日期**：2026-11-22
- **目标**：将 OpenMausBot 数据目录从 C 盘迁至 F 盘，原路径留目录联接（junction）保持兼容
- **涉及工具**：`migrate-openmausbot.ps1`（robocopy /MOVE + junction 方案）

---

## 一、最终结果

```
F:\DevelopEnv\<应用数据目录>\
├── .openmausbot\            ← 主数据，1705 个文件（含 config.json）
└── .openmausbot-companion\  ← companion 数据（devices.json）

C:\Users\<用户名>\.openmausbot           ──junction──►  F:\DevelopEnv\<应用数据目录>\.openmausbot
C:\Users\<用户名>\.openmausbot-companion ──junction──►  F:\DevelopEnv\<应用数据目录>\.openmausbot-companion
```

- 全程**零数据丢失**：每次变更前后文件数核对一致（主数据 1705 / genshin-tracker 113 / companion 1）。
- 所有程序继续使用 C 盘老路径，经 junction 转发实际读写 F 盘。
- `migrate-openmausbot.ps1` 已同步更新目标路径，UTF-8 BOM 完好，PS 5.1 解析通过。

---

## 二、过程时间线

### 阶段 1：脚本执行失败排查（初始问题）

**现象**：脚本第 2 步 robocopy 报"错误 32（0x00000020）另一个程序正在使用此文件"，锁定对象为
`task-workspaces\...\genshin-tracker\`；第 3 步 `Remove-Item` 死在同一把锁上，junction 未建成。

**排查过程**：
1. 清点现场：源目录仅剩 42 个**空目录壳**；genshin-tracker 的 113 个文件（含完整 `.git`）已全部落到 F 盘——**数据无损，只是壳删不掉**。
2. 进程普查（按进程名、可执行路径、命令行）均未命中，脚本第 1 步按名称检查 OpenMausBot/Magpie 也确实没在运行。
3. 关键手段：通过 `NtQueryInformationProcess` 读各进程 PEB 拿到**工作目录（CWD）**，全量扫描命中——
   **PID 25652 `python -m http.server 8788`**（当天 09:02 启动，疑似历史任务遗留的预览服务器），CWD 恰好钉在 genshin-tracker 里。

**根因**：进程 CWD 指向某目录时，Windows 不允许删除/移动该目录本身。robocopy 能搬走目录内容，但删不掉目录壳；两个报错同源。

**处理**：结束该进程 → 删除 42 个空壳 → 创建 junction → 验证通过（LinkType=Junction，老路径可见 1705 文件，config.json 可访问）。

### 阶段 2：目标路径改为 `F:\DevelopEnv\<应用数据目录>`（第一轮）

- 方式：`cmd /c rmdir` 只删 junction 重解析点（不碰目标数据）→ F 盘同卷 `Rename-Item`（瞬时完成）→ 重建 junction。
- 中途两个插曲：
  1. 目标 `OpenMausBotData` 已存在（当天 13:27 创建的空目录）。核实为空后放行：先删空目录再改名。
  2. 一次误报 ABORT"不是 junction"——实为脚本编辑时丢失 `$j = Get-Item` 赋值行导致检查读到空值，修复后成功。**实际状态一直是好的**。

### 阶段 3：需求澄清后的嵌套调整（最终布局）

用户澄清期望：数据应放在 `OpenMausBotData` **目录之下**（`.openmausbot` 子目录），而非直接铺在外层。

- 方式：`OpenMausBotData` → 改名为临时目录 → 新建空的 `OpenMausBotData` 外层容器 → 数据整体移入 `OpenMausBotData\.openmausbot` → 重建 junction 指向嵌套路径。
- 验证：Target 精确匹配、1705 文件前后一致、config.json 双路径可达、临时目录清理干净。

### 阶段 4：`OMB_DATA_DIR` 疑问澄清

脚本注释建议"OpenMausBot 必须设置环境变量 OMB_DATA_DIR=..."。核实结果：

- 系统 User / Machine 级环境变量均未设置过；
- `config.json` 中无数据目录配置项（仅 `configDir` 指向 C 盘老路径，经 junction 转发后照常工作）；
- app-docs 内无任何 DATA_DIR 相关文档。

**结论**：`OMB_DATA_DIR` 是脚本作者的**假设性建议**，并非 OpenMausBot 已证实的配置项。当前无需设置——junction 已完整覆盖需求。若日后确认程序支持该变量，可设为 `F:\DevelopEnv\<应用数据目录>\.openmausbot` 彻底摆脱 C 盘依赖。

### 阶段 5：companion 目录迁移

- 对象：`C:\Users\<用户名>\.openmausbot-companion`（仅 1 个文件 `devices.json`，358 B）。
- 迁移前 CWD 锁扫描无占用；robocopy /MOVE 完成后**连源目录壳一并删除**（与阶段 1 大目录残留空壳的行为不同），脚本按上次的假设执行 `Remove-Item` 时报"路径不存在"而中断——属预期行为差异，非故障。
- 补建 junction 并验证：老路径可读 devices.json、内容完整。

---

## 三、问题与解决汇总

| # | 问题 | 根因 | 解决 |
|---|------|------|------|
| 1 | robocopy 错误 32 / Remove-Item 失败 | 遗留 python http.server 进程 CWD 钉在 genshin-tracker | 杀进程、删空壳、建 junction |
| 2 | 按进程名/路径/命令行排查均未命中 | CWD 占用不体现在常规进程属性里 | PEB 读 CWD 全量扫描（可复用手段） |
| 3 | 目标目录已存在 | 用户侧预建的空目录 | 核实为空后删除放行 |
| 4 | 误报"源不是 junction" | 迁移脚本编辑时丢失一行赋值，检查读到空值 | 修复脚本后重跑 |
| 5 | PS 5.1 报"字符串缺少终止符" | 编辑重写丢失 UTF-8 BOM，中文被按 GBK 误解码、字节序列吞掉引号 | 补回 BOM 后解析通过 |
| 6 | companion 迁移中 Remove-Item 报路径不存在 | 小目录场景 robocopy /MOVE 连壳一起删了 | 识别为正常结果，直接补建 junction |
| 7 | 内联 PowerShell 命令变量被吞（会话工具层） | 外层 shell 展开 `$` | 改为落盘 .ps1 文件执行 |

---

## 四、经验与注意事项

1. **"目录内容已空但删不掉/挪不动" ≈ CWD 占用**。常规进程名/路径/命令行扫描查不到，需要读进程 PEB 的 CurrentDirectory。git fsmonitor--daemon 等后台进程是无辜的，不要误杀。
2. **含中文的 .ps1 必须 UTF-8 with BOM**。Windows PowerShell 5.1 对无 BOM 文件按 ANSI/GBK 解码，中文与引号的字节交错会产生"字符串缺少终止符"这类误导性报错（报错行号可能指向完全正常的行）。今后编辑该脚本务必保持 UTF-8 with BOM。
3. **删 junction 用 `cmd /c rmdir`**：只移除重解析点，绝不触碰目标数据；`Remove-Item -Recurse` 对 junction 有穿透风险。
4. **robocopy /MOVE 的壳清理行为不恒定**（大目录残留空壳、小目录连壳删），自动化脚本不能假设其一。
5. **F 盘同卷 rename/move 是瞬时操作**，路径调整成本极低；跨盘才需要 robocopy。
6. 迁移类操作前先做锁扫描（CWD + 可执行路径），并记录文件数基线，事后核对实现"零丢失"可证明。

---

## 五、当前系统状态

| 项 | 状态 |
|----|------|
| `C:\Users\<用户名>\.openmausbot` | Junction → `F:\DevelopEnv\<应用数据目录>\.openmausbot`（1705 文件） |
| `C:\Users\<用户名>\.openmausbot-companion` | Junction → `F:\DevelopEnv\<应用数据目录>\.openmausbot-companion`（1 文件） |
| `F:\DevelopEnv\.openmausbot`（旧目标名） | 已不存在（先后经历改名、嵌套两次调整） |
| `migrate-openmausbot.ps1` | 目标路径已更新为嵌套布局；BOM 完好；PS 5.1 解析通过 |
| `OMB_DATA_DIR` | 未设置，也无需设置（非证实存在的配置项） |
| 遗留 python http.server | 已终止 |
