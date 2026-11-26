# CLI 约定（供 agent 与脚本调用）

`link-manager` CLI 面向 agent / 脚本设计：输出稳定、退出码有据、非交互环境绝不挂起。本文是唯一权威约定；实现以 `src/LinkManager.Cli/Program.cs` 的 `ExitCodes` 为准，改动须双向同步。

## 命令一览

| 命令 | 用法 | 说明 |
|------|------|------|
| `version` | `link-manager version` | 打印名称与版本 |
| `ls` | `link-manager ls` | 列出登记链接与健康状态 |
| `create` | `link-manager create <源路径> <目标路径> [备注]` | 创建 junction 并自动登记 |
| `scan` | `link-manager scan <目录> [--yes] [--max-depth <层数>]` | 扫描存量链接，问询登记（`--yes` 全量导入） |
| `repair` | `link-manager repair <源路径>` | 按登记表重建断链（不删除真实数据或改登记） |
| `note` | `link-manager note <源路径> <新备注>` | 更新登记条目备注（空串清除） |
| `remove` | `link-manager remove <源路径> --keep-link \| --delete-link [--yes]` | 删登记（`--delete-link` 连链接一起删，需 `--yes`） |
| `config` | `link-manager config \| config set-registry <新登记文件路径>` | 显示/迁移登记文件位置 |
| `migrate` | `link-manager migrate <源路径> <目标路径> [备注] [--execute]` | 迁移目录并回指 junction（默认 dry-run） |
| `help` | `link-manager help`（或 `--help` / `-h`） | 打印命令帮助 |

## 通用选项

- `--json` — 所有子命令支持。输出单行紧凑 JSON 到 **stdout**，可被 `jq` 直接解析；人类可读输出不变（仍走原格式）。
- `--registry <根目录>`（或 `--registry=<根目录>`）— 把登记表重定向到 `<根目录>/link-manager/registry.json`。默认为用户 AppData。供 agent / 测试沙箱隔离用，放在命令名之后的任意位置。
- `--json` 可放在命令名之后的任意位置（即使其余参数有误，失败时仍输出 JSON 包络而非纯文本；命令名本身在第一位，全局选项不能放在它之前）。
- 未知命令 → 退出码 64；已知命令带未知选项 → 退出码 1（usage）。选项错误优先于未知命令判定（命令名合法才进入未知选项检查——实际实现先解析选项，故 `foo --badopt` 退出 1）。
- 子命令自有选项（如 `create --symlink`、`migrate --execute`、`remove --yes`、`scan --yes` / `--max-depth`）由全局解析器按命令白名单放行，带值选项支持空格与 `=` 两种形式；具体校验归命令自己。新命令加自有选项时须登记进 `Program.cs` 的 `CliCommandFlags`。
- 无参数时打印一行版本 + 命令列表后立即退出（退出码 0），**不进入交互等待**。

## 退出码

| 码 | 常量 | 含义 | 触发路径 |
|----|------|------|----------|
| `0` | `Success` | 成功 | 命令正常完成 |
| `1` | `UserError` | 用户错误（用法/参数） | `create` 参数不足、`scan` 目录不存在、未知选项 |
| `2` | `ExecutionFailed` | 执行失败 | junction 创建失败（护栏拒绝、PowerShell 失败等）；`note`/`remove`/`config`/`migrate` 用法错误与目标未找到；`migrate` 计划被阻碍（进程运行中）；`migrate --execute` 迁移失败（护栏拒绝/复制核对失败/登记失败已回滚） |
| `3` | `RegistryError` | 登记表错误 | registry.json 损坏或读写失败 |
| `4` | `AbnormalLinks` | 仅 `ls`：成功但发现异常链接 | 登记表含断链/缺失/伪装目录条目 |
| `64` | `UnknownCommand` | 未知命令 | 命令名不在支持列表 |


agent 判读建议：先按退出码分支；`--json` 模式下再读 `.ok` 与 `.error.code` 细分原因（文本模式错误走 **stderr**）。

## --json 输出包络

单行 JSON，顶层固定字段：

```json
{"ok":true,"command":"ls","data":{...}}
{"ok":false,"command":"create","error":{"code":"usage","message":"用法：create <源路径> <目标路径> [备注]"}}
```

- 成功：`ok=true`、`command=<命令名>`、`data=<命令结果>`；**没有** `error` 字段。
- 失败：`ok=false`、`command=<命令名>`、`error={code,message}`；**没有** `data` 字段。
- `error.code` ∈ `usage | execution_failed | registry_error | unknown_command`（与退出码一一对应，见上表）。
- 中文与反斜杠原样输出（不转 `\uXXXX`）；输出为单行，逐行读取安全。
- stdout 编码固定 **UTF-8**（不随系统 OEM 码页变化）；Windows 重定向/管道下中文备注与错误消息均为合法 UTF-8。stderr（文本模式错误通道）同样固定 UTF-8。

### 各命令 data 形状

**version**：

```json
{"ok":true,"command":"version","data":{"name":"link-manager","version":"1.0.0"}}
```

**ls**（`status` 为即时健康检查结果，英文小写）：

```json
{"ok":true,"command":"ls","data":{"entries":[{"source":"C:\\A","target":"F:\\B","type":"junction","note":"","status":"ok"}],"registry":{"path":"…\\registry.json","entryCount":1,"abnormalCount":0}}}
```

**create**（返回新登记条目 + 即时健康状态）：

```json
{"ok":true,"command":"create","data":{"source":"C:\\链接处","target":"F:\\真实目录","type":"junction","note":"","status":"ok"}}
```

**scan**（候选列表 + 跳过目录 + 汇总；`importedCount` 为本次实际登记的条数，纯报告时为 0）：

```json
{"ok":true,"command":"scan","data":{"links":[{"source":"C:\\链接处","target":"F:\\真实目录","type":"junction"}],"skipped":["C:\\无权限目录"],"summary":{"candidateCount":1,"skippedCount":1,"registeredCount":0,"scannedDirectoryCount":5,"importedCount":0}}}
```

**note**（返回更新后的条目，形状同 create）：

```json
{"ok":true,"command":"note","data":{"source":"C:\\链接处","target":"F:\\真实目录","type":"junction","note":"新备注","status":"ok"}}
```

**remove**（`keepLink=true` 表示仅删登记、链接保留）：

```json
{"ok":true,"command":"remove","data":{"source":"C:\\链接处","keepLink":true}}
```

**config**（`customRegistryFilePath` 为用户自定义路径，无自定义时省略）：

```json
{"ok":true,"command":"config","data":{"registryFilePath":"C:\\Users\\…\\AppData\\Roaming\\link-manager\\registry.json","customRegistryFilePath":"D:\\registry.json"}}
```

**migrate**（dry-run 与 `--execute` 共用形状；`executed=false` 为 dry-run。`runningProcesses` 非空时计划被阻碍，退出码 2；报告全文仅在文本模式）：

```json
{"ok":true,"command":"migrate","data":{"source":"C:\\源目录","target":"F:\\目标目录","steps":["前置检查：源目录存在（C:\\源目录）","…"],"runningProcesses":["magpie"],"executed":false}}
```

**repair**（成功时返回重建后的登记条目 + 即时健康状态；未找到登记、目标不存在、链接位置被占用、相关进程运行或类型不支持均为退出码 `2` / `execution_failed`）：

```json
{"ok":true,"command":"repair","data":{"source":"C:\\链接处","target":"F:\\真实目录","type":"junction","note":"","status":"ok"}}
```


```json
{"ok":true,"command":"help","data":{"commands":[{"name":"version","description":"…"}],"options":["--json"]}}
```

`status` 取值：`ok | broken | missing | disguised`（语义同 GUI/文本模式四态）。

## 非交互保证

- CLI 不读 stdin，不等待回车；无参数、缺参数、坏参数一律立即打印结果并以明确退出码退出。
- `scan` 是唯一可能读 stdin 的命令（交互问询）：stdin 不可读（重定向/管道/CI）时自动降级为只报告不问询，绝不挂起；要全量导入请加 `--yes`。
- junction 创建内部调用 PowerShell 时使用 `-NoProfile -EncodedCommand`，输出重定向并设 15 秒超时（超时杀进程报错），不会挂起。
- 适合 CI / agent 循环调用：永远"快速成功或快速失败"。

## 构建与发布

需要 .NET 11 SDK（`global.json` 锁定 11.0.100；本机 SDK 经 dnvm 管理）。

```sh
dotnet build            # 构建（Debug）
dotnet test             # 全量测试
dotnet publish src/LinkManager.Cli/LinkManager.Cli.csproj -c Release
```

发布产物：`src/LinkManager.Cli/bin/Release/net11.0/win-x64/publish/LinkManager.Cli.exe` —— **自包含单文件**（运行时内嵌、压缩启用），无需安装 .NET，可拷贝到任意 win-x64 机器双击或命令行运行。csproj 对应配置：`PublishSingleFile`、`SelfContained`、`RuntimeIdentifier=win-x64`、`EnableCompressionInSingleFile`。
