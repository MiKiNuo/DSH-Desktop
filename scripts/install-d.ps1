<#
.SYNOPSIS
    将 DSH Desktop 安装到 D 盘，避开系统盘（C 盘）占用。

.DESCRIPTION
    Velopack 的 one-click Setup.exe 固定装到 %LocalAppData%\{packId}（C 盘），打包期无参数可改；
    --installto 是官方提供的唯一安装期覆盖手段（https://docs.velopack.io/packaging/installer）。
    本脚本不改系统 programFilesDir，也不触碰同机并存的 DSH Desktop（Electron 版，目录名含空格）。

.PARAMETER InstallDir
    安装目录。默认 D:\Program Files\DSH-Desktop。

.PARAMETER SetupExe
    Setup.exe 路径。省略时自动取 releases 目录下最新一个。

.EXAMPLE
    .\install-d.ps1

.EXAMPLE
    .\install-d.ps1 -InstallDir 'D:\Program Files\DSH-Desktop'
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'D:\Program Files\DSH-Desktop',
    [string]$SetupExe
)

$ErrorActionPreference = 'Stop'

# 同机并存另一款 DSH（Electron 版，目录名含空格）。绝不允许装到它的目录。
$ownedInstallDir = 'D:\Program Files\DSH-Desktop'
$foreignInstallDir = 'D:\Program Files\DSH Desktop'
if ($InstallDir.TrimEnd('\') -ieq $foreignInstallDir) {
    throw "拒绝安装到 '$InstallDir'：该目录属于同机的另一款 DSH 软件，请改用 '$ownedInstallDir'。"
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SetupExe) {
    $SetupExe = Get-ChildItem -Path (Join-Path $repoRoot 'releases') -Filter '*-Setup.exe' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $SetupExe -or -not (Test-Path $SetupExe)) {
    throw '找不到 Setup.exe。请先执行 release 打包，或用 -SetupExe 显式指定路径。'
}

Write-Host "安装包  : $SetupExe"
Write-Host "目标目录: $InstallDir"

Start-Process -FilePath $SetupExe -ArgumentList '--installto', "`"$InstallDir`"" -Wait

if (Test-Path (Join-Path $InstallDir 'current\DshDesktop.App.exe')) {
    Write-Host "安装成功：$InstallDir"
}
else {
    throw "安装后未在 $InstallDir 找到程序，请检查安装日志。"
}
