# install.ps1 —— 侧载安装本扩展的 MSIX（务必以【管理员身份】运行）
#
# 为什么需要管理员：
#   MSIX 的签名校验由 AppX 部署服务在【机器级】完成（0x800B0109 根证书不受信任）。
#   仅把证书装进「当前用户\受信任人」不够，必须装进「本地计算机\受信任人」——这需要管理员权限。
#   注意：本证书不是 CA 证书（不能带 BasicConstraints CA:TRUE，否则 MakeAppx 签名报 APPX0107），
#   它装不进「受信任的根」，也不需要。
#
# 为什么先卸载再装：
#   直接覆盖升级时，如果旧的扩展进程还在跑，旧包会被标记为 superseded 但注册不会立刻消失，
#   AppExtension 目录会同时列出新旧两个版本 —— 命令面板会把同一套命令加载两遍
#   （表现为列表里出现两组一模一样的入口）。所以先彻底移除同名包，再装新的。
#
# 用法：右键 PowerShell -> 以管理员身份运行，然后：
#   cd D:\scripts\PowerEverything
#   .\install.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$cer = Join-Path $root 'src\PowerEverything_TemporaryKey.cer'
$pkgName = 'PowerEverything'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "请以【管理员身份】重新运行本脚本（开始菜单搜 PowerShell -> 右键 -> 以管理员身份运行）。" -ForegroundColor Red
    exit 1
}

# 1) 证书不存在则先生成
if (-not (Test-Path $cer)) {
    & (Join-Path $root 'setup-cert.ps1')
}

# 2) 装证书到 本地计算机\受信任人（侧载信任）
#
#    ★ 只装「受信任人」，不要装「受信任的根」：这张证书是代码签名证书、不是 CA，
#      而 Root 存储要求 BasicConstraints CA:TRUE，往里装必定报「证书缺少基本约束扩展」——
#      那不是需要修的故障，侧载也根本不需要 Root。装错位置是这条报错的唯一来源。
try {
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople -ErrorAction Stop | Out-Null
} catch {
    Write-Host "装证书失败：$($_.Exception.Message)" -ForegroundColor Red
    Write-Host "（本地计算机的证书存储只有管理员能写，请确认这个窗口是管理员身份。）" -ForegroundColor Yellow
    exit 1
}

# 2b) 回读确认证书真的进了本机受信任人存储 —— 侧载报 0x800B0109 基本都是卡在这一条
$cerObj = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $cer
$trusted = @(Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $cerObj.Thumbprint })
if ($trusted.Count -eq 0) {
    Write-Host "证书没有真正进入 本地计算机\受信任人，侧载必然失败（0x800B0109）。" -ForegroundColor Red
    exit 1
}
Write-Host "已安装并确认本机信任证书（指纹 $($cerObj.Thumbprint)）。" -ForegroundColor Green

# 3) 找到最新的 .msix（跳过 installer 辅助目录里的东西）
$apRoot = Join-Path $root 'Release\AppPackages'
$msix = Get-ChildItem $apRoot -Recurse -Filter *.msix -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\Dependencies\\' } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) {
    Write-Error "未找到 .msix，请先运行 .\build.ps1 生成包。"
    exit 1
}

# 4) 结束相关进程：本扩展的进程 + 命令面板本体（否则它会一直握着旧的扩展目录）
Get-Process -Name 'PowerEverything' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name 'Microsoft.CmdPal.UI' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# 5) 先彻底移除同名包（含所有残留版本），再装新的
$old = Get-AppxPackage -Name $pkgName -ErrorAction SilentlyContinue
if ($old) {
    foreach ($p in $old) {
        Write-Host "移除旧包: $($p.PackageFullName)" -ForegroundColor Yellow
        Remove-AppxPackage -Package $p.PackageFullName -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 800
}

Write-Host "正在安装: $($msix.FullName)" -ForegroundColor Cyan
try {
    Add-AppxPackage -Path $msix.FullName -ForceUpdateFromAnyVersion -ForceApplicationShutdown -ErrorAction Stop
} catch {
    Write-Host "侧载失败: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  · 0x800B0109「根证书必须是受信任的证书」→ 证书没进「本地计算机\受信任人」（见上面第 2 步）" -ForegroundColor Yellow
    Write-Host "  · 0x80073CF3「找不到框架」→ 先装 Release\AppPackages\<包目录>\Dependencies\x64\Microsoft.WindowsAppRuntime.1.8.msix" -ForegroundColor Yellow
    exit 1
}
Write-Host "安装完成。" -ForegroundColor Green

Write-Host ""
Write-Host "当前已安装的相关包：" -ForegroundColor Cyan
Get-AppxPackage *PowerEverything* | Select-Object Name, Version, Status | Format-Table -AutoSize

# 6) 自检：确认 AppExtension 目录里只剩一个版本
Write-Host "自检：命令面板扩展目录中的本扩展条目" -ForegroundColor Cyan
try {
    $null = [Windows.ApplicationModel.AppExtensions.AppExtensionCatalog, Windows.ApplicationModel.AppExtensions, ContentType=WindowsRuntime]
    $cat = [Windows.ApplicationModel.AppExtensions.AppExtensionCatalog]::Open('com.microsoft.commandpalette')
    $mine = @($cat.FindAll() | Where-Object { $_.Package.Id.Name -eq $pkgName })
    Write-Host ("  count = " + $mine.Count)
    foreach ($e in $mine) { Write-Host ("  " + $e.Package.Id.FullName + "  (" + $e.DisplayName + ")") }
    if ($mine.Count -gt 1) {
        Write-Host "  ⚠ 仍有多个版本被注册，请重启一次资源管理器后再运行本脚本。" -ForegroundColor Yellow
    }
} catch {
    Write-Host ("  （目录查询不可用：" + $_.Exception.Message + "）") -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "下一步：重新打开命令面板（开始菜单搜 Command Palette）。" -ForegroundColor Cyan
Write-Host "主页应同时出现 QuickJump 跳转入口与 Everything 的两个搜索入口。" -ForegroundColor Cyan
