# linkmgr

Windows 链接（junction / symlink）管理工具：把「C 盘目录搬去其他盘 + 原地回指」这件易错的事变成安全的向导式操作。GUI 给人用，CLI 给 agent 用。

> 项目来由：本仓库前身为一次性迁移脚本 data_mover（OpenMausBot 数据迁移），完整历史见 `archive/data-mover` 分支。

## 状态

v1 开发中。设计与工单见 GitHub Issues（spec #1，工单 #2–#20）。

## 架构

- `LinkMgr.Core` — 全部业务逻辑，无 UI 依赖
- `LinkMgr.Cli` — 命令行前端（agent 调用；无参数时进入交互向导）
- `LinkMgr.Gui` — WPF 桌面前端

## 构建

需要 .NET 10 SDK。

```
dotnet build
dotnet test
```
