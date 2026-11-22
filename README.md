# link-manager

Windows 链接（junction / symlink）管理工具：把「C 盘目录搬去其他盘 + 原地回指」这件易错的事变成安全的向导式操作。GUI 给人用，CLI 给 agent 用。

> 项目来由：本仓库前身为一次性迁移脚本 data_mover（OpenMausBot 数据迁移），完整历史见 `archive/data-mover` 分支。

## 状态

v1 开发中。设计与工单见 GitHub Issues（spec #1，工单 #2–#20）。

## 架构

- `LinkManager.Core` — 全部业务逻辑，无 UI 依赖
- `LinkManager.Cli` — 命令行前端（agent 调用；支持 `--json` 结构化输出，约定见 `docs/cli-conventions.md`）
- `LinkManager.Gui` — WPF 桌面前端

## 构建

需要 .NET 11 SDK（global.json 锁定 11.0.100；本机 SDK 经 dnvm 管理）。

```
dotnet build
dotnet test
```

## 发布（自包含单文件 exe）

```sh
dotnet publish src/LinkManager.Cli/LinkManager.Cli.csproj -c Release
```

产物：`src/LinkManager.Cli/bin/Release/net11.0/win-x64/publish/LinkManager.Cli.exe` —— 自包含单文件（运行时内嵌、压缩启用），无需安装 .NET，拷到任意 win-x64 机器即可双击或命令行运行。
