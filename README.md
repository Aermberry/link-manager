<p align="center"><img src="docs/logo.png" width="240" alt="link-manager logo"></p>

# link-manager

**把「C 盘目录搬去其他盘 + 原地回指」这件易错的事，变成向导式、可回滚、可审计的操作。**

微信记录、游戏目录、模型缓存、开发环境——程序把路径写死在配置里，想给 C 盘腾地方，只能把目录搬走再原地留一个 junction。手工 `mklink` 成功一次不难，难的是搬一半出错、进程还在写、链接类型选错。link-manager 把这条路修平：**GUI 向导给人用，CLI 给 agent 用**，默认 dry-run，带护栏，全程可查。

![.NET](https://img.shields.io/badge/.NET-11.0-blueviolet) ![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows11&logoColor=white) ![License](https://img.shields.io/badge/license-MIT-green)

## 这适合你吗？

适合你，如果你遇到过：

- **C 盘快满，但软件不提供改路径的设置**——只能搬目录 + 回指 junction。
- **搬过一次，心有余悸**——不知道搬的时候哪个进程还在写，搬完也不确定链接是否健康。
- **已经有了一批手工 `mklink` 出来的链接**——想登记造册，随时检查有没有断链、缺链、伪装目录。
- **想让 agent 替你干**——CLI 有稳定的 JSON 输出、明确的退出码、绝不交互挂起。

不适合，如果：目标目录能在软件自己的设置里改位置（优先用官方设置）；或者你在 Linux/macOS 上——junction/symlink 回指是 Windows 的机制。

## 快速开始

需要 Windows 10+ 与 .NET 11 SDK（`global.json` 锁定 11.0.100）：

```sh
dotnet build
dotnet run --project src/LinkManager.Gui   # 图形向导
```

CLI 的典型迁移流程——**默认 dry-run，先看计划再执行**：

```sh
link-manager migrate "C:\Users\you\BigDir" "F:\Data\BigDir"            # 只出迁移计划，不动磁盘
link-manager migrate "C:\Users\you\BigDir" "F:\Data\BigDir" --execute  # 确认无误，真正执行
link-manager ls                                                        # 随时检查全部链接健康状态
```

发布产物为自包含单文件 exe（无需安装 .NET，拷到任意 win-x64 机器即可运行），见[构建](#构建)。

## 命令一览

| 命令 | 用法 | 说明 |
|------|------|------|
| `migrate` | `link-manager migrate <源> <目标> [备注] [--execute]` | 迁移目录并回指 junction（默认 dry-run） |
| `create` | `link-manager create <源> <目标> [备注] [--symlink]` | 创建链接并自动登记（默认 junction；`--symlink` 需管理员或开发者模式） |
| `scan` | `link-manager scan <目录> [--yes] [--max-depth <n>]` | 扫描存量链接并问询登记（`--yes` 全量导入） |
| `ls` | `link-manager ls` | 列出登记链接与健康状态 |
| `note` | `link-manager note <源> <新备注>` | 更新登记条目备注 |
| `remove` | `link-manager remove <源> --keep-link \| --delete-link [--yes]` | 删登记（`--delete-link` 连链接一起删，需 `--yes`） |
| `config` | `link-manager config \| config set-registry <路径>` | 显示 / 迁移登记文件位置 |
| `version` | `link-manager version` | 打印名称与版本 |
| `help` | `link-manager help` | 打印帮助 |

全局选项：`--json`（结构化输出）、`--registry <根目录>`（登记表重定向，agent 沙箱隔离用）。登记表默认在 `%APPDATA%\link-manager\registry.json`。

## 为 agent 而设计

CLI 面向脚本与 agent 调用：输出稳定、退出码有据、非交互环境绝不挂起。完整约定见 [`docs/cli-conventions.md`](docs/cli-conventions.md)。

```sh
$ link-manager ls --json
{"ok":true,"command":"ls","data":{"entries":[{"source":"C:\\链接处","target":"F:\\真实目录","type":"junction","note":"","status":"ok"}],"registry":{"path":"…","entryCount":1,"abnormalCount":0}}}
```

退出码：`0` 成功 · `1` 用法错误 · `2` 执行失败 · `3` 登记表损坏 · `4` `ls` 发现异常链接 · `64` 未知命令。CLI 不读 stdin（`scan` 在 stdin 不可读时自动降级为只报告），永远「快速成功或快速失败」。

## 安全设计

- **默认 dry-run**：`migrate` 不带 `--execute` 只输出分步计划，不碰磁盘。
- **护栏**：源目录有进程运行中则拒绝执行；junction 创建经护栏校验，拒绝危险路径。
- **失败回滚**：迁移后半段失败（如登记失败）自动回滚，不留半成品。
- **删除需要确认**：连链接一起删必须显式 `--yes`；仅删登记（`--keep-link`）不碰磁盘上的链接。
- **沙箱友好**：`--registry` 把登记表重定向到指定目录，agent / 测试不污染真实登记。

## 架构

- [`LinkManager.Core`](src/LinkManager.Core) — 全部业务逻辑（登记、迁移、扫描、健康检查），无 UI 依赖
- [`LinkManager.Cli`](src/LinkManager.Cli) — 命令行前端，`--json` 约定见 `docs/cli-conventions.md`
- [`LinkManager.Gui`](src/LinkManager.Gui) — WPF 桌面向导

172 个测试（Core 118 + Cli 43 + GUI 11）：`dotnet test` 全绿。

## 构建

```sh
dotnet build
dotnet test
dotnet publish src/LinkManager.Cli/LinkManager.Cli.csproj -c Release
```

发布产物：`src/LinkManager.Cli/bin/Release/net11.0/win-x64/publish/LinkManager.Cli.exe` —— 自包含单文件（运行时内嵌、压缩启用），无需安装 .NET。

## 功能进度

各版本功能与可测试项汇总见 [功能进度工作台](docs/dashboard.html)。

## 项目来由

本仓库前身为一次性迁移脚本 data_mover（OpenMausBot 数据迁移），完整历史见 `archive/data-mover` 分支。

## License

[MIT](LICENSE)
