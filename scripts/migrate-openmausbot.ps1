# ============================================================
#  OpenMausBot 数据目录迁移脚本
#       C:\Users\<用户名>\.openmausbot  ->  F:\DevelopEnv\<应用数据目录>\.openmausbot
# ============================================================
# 用法：先退出 OpenMausBot（包括托盘）和 Magpie，然后右键本脚本
#       "使用 PowerShell 运行"。
#
# 原理：robocopy /MOVE 支持超长路径，把数据整体搬到 F 盘，
#       然后在原位置留一个目录联接 (junction) 指向新位置，
#       所有程序仍然用老路径访问，无需改任何配置。
# ============================================================

$ErrorActionPreference = 'Stop'
$dst = "F:\DevelopEnv\<应用数据目录>\.openmausbot"
$src = Join-Path $env:USERPROFILE ".openmausbot"

# ---- 前置检查 ----
Write-Host "=== 第 1 步：前置检查 ===" -ForegroundColor Cyan

$procs = Get-Process | Where-Object { $_.ProcessName -match '(?i)openmausbot|magpie' }
if ($procs) {
    Write-Host "以下程序还在运行，请先退出它们再运行本脚本：" -ForegroundColor Red
    $procs | Select-Object Id, ProcessName | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "（OpenMausBot 要从托盘图标退出；Magpie 同理）" -ForegroundColor Yellow
    Read-Host "按回车键退出脚本"
    exit 1
}
Write-Host "OK：OpenMausBot 与 Magpie 均未在运行。" -ForegroundColor Green

if (-not (Test-Path "$dst")) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }
$existing = Get-ChildItem $dst -Force -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "目标目录 $dst 不是空的。为避免覆盖，请换一个空目录或先清空它。" -ForegroundColor Red
    Read-Host "按回车键退出脚本"
    exit 1
}

# ---- 复制 ----
Write-Host "`n=== 第 2 步：robocopy 迁移数据（约 0.33 GB，预计几十秒） ===" -ForegroundColor Cyan
# /MOVE = 复制后删除源文件   /E = 包含空目录   /COPY:DAT = 数据/属性/时间戳
# /COPYALL 备用；/R:2 /W:2 重试策略；/XJ 不跟随目录联接，避免死循环
robocopy $src $dst /E /MOVE /COPY:DAT /DCOPY:DAT /R:2 /W:2 /XJ /NFL /NDL /NP
if ($LASTEXITCODE -ge 8) {
    Write-Host "robocopy 退出码 $LASTEXITCODE 表示有文件复制失败，请把上方日志截图发我。" -ForegroundColor Red
    Read-Host "按回车键退出脚本"
    exit 1
}
# robocopy 退出码 0-7 都算成功（1=有文件复制）
Write-Host "robocopy 完成，退出码 $LASTEXITCODE（0-7 均为成功）。" -ForegroundColor Green

# ---- 建立目录联接 ----
Write-Host "`n=== 第 3 步：在原位置创建目录联接 ===" -ForegroundColor Cyan
if (Test-Path $src) {
    # /MOVE 之后 robocopy 应已清空源目录；若目录壳还在，删掉再建链接
    Remove-Item $src -Recurse -Force
}
New-Item -ItemType Junction -Path $src -Target $dst | Out-Null
Write-Host "OK：junction 已创建：$src -> $dst" -ForegroundColor Green

# ---- 验证 ----
Write-Host "`n=== 第 4 步：验证 ===" -ForegroundColor Cyan
$j = Get-Item $src -Force
if ($j.LinkType -eq 'Junction' -and (Test-Path "$src\config.json")) {
    $n = (Get-ChildItem $src -Recurse -Force -File | Measure-Object).Count
    Write-Host "验证通过：junction 生效，通过老路径能看到 $n 个文件（含 config.json）。" -ForegroundColor Green
    Write-Host "`n全部完成！现在可以重新启动 OpenMausBot 和 Magpie。" -ForegroundColor Green
    Write-Host "注意：OpenMausBot 必须设置环境变量 OMB_DATA_DIR=F:\DevelopEnv\<应用数据目录>\.openmausbot" -ForegroundColor Yellow
    Write-Host "（有 junction 在，不设也能工作，但设置后可彻底摆脱对 C 盘老路径的依赖。）" -ForegroundColor Yellow
} else {
    Write-Host "验证未通过，请截图发我。" -ForegroundColor Red
}

Read-Host "`n按回车键关闭窗口"
