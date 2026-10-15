# build.ps1
# 1) 先准备自签名证书（首次运行会生成并写入 csproj；之后幂等跳过）
# 2) 用 scoop 的 .NET 10 SDK 执行 dotnet publish，产出已签名 MSIX。
#    本项目用 Microsoft.Windows.SDK.BuildTools.MSIX 打包，MSIX 生成挂在 Publish 生命周期：
#    必须在发布配置(win-x64.pubxml / win-arm64.pubxml)里设 PublishAppxPackage=true 才会真正出包。
#    csproj 已设 <PublishProfile>win-$(Platform).pubxml</PublishProfile> 指向这两个文件。
#
# 关于 MSB3231（无法移除目录 ... 被另一个进程占用）：
#   打包目标在出包前会先删除旧的 <AppxPackageDir>\<包名>_<版本>_<架构>_Test\ 再重建。
#   若该目录被资源管理器窗口 / 杀软 / 索引器占着句柄，删除就会失败并中断构建。
#   本脚本先尽力清理旧输出；若发现仍有目录删不掉（被占用），则本次自动改用「带时间戳的唯一目录」，
#   绕开那个被锁的旧目录，保证构建能继续。
#
# 用法：在仓库根目录执行  .\build.ps1  （默认 Release|x64；需 Debug 自行加 -c Debug；需 ARM64 加 -p:Platform=ARM64）
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

$setup = Join-Path $root 'setup-cert.ps1'
if (Test-Path $setup) { & $setup }

$scoopDotnet = Join-Path $env:USERPROFILE 'scoop\apps\dotnet-sdk\current\dotnet.exe'
if (-not (Test-Path $scoopDotnet)) {
    Write-Error "未找到 scoop 的 dotnet SDK: $scoopDotnet`n请先安装: scoop install dotnet-sdk"
    exit 1
}

Write-Host "使用 dotnet: $scoopDotnet" -ForegroundColor Cyan

$proj = Join-Path $root 'src\PowerEverything.csproj'

# --- 输出目录：先尽力清理，仍被占用则退回唯一目录（绕开 MSB3231） ---
$apRoot = Join-Path $root 'Release\AppPackages'
if (Test-Path $apRoot) {
    Get-ChildItem $apRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        try { Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop } catch { }
    }
}
$stillLocked = @(Get-ChildItem $apRoot -Directory -ErrorAction SilentlyContinue).Count -gt 0
if ($stillLocked) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $appxDir = Join-Path $apRoot "build-$stamp"
    Write-Host "旧的输出目录被占用（可能被资源管理器/杀软锁定），本次改用唯一目录：" -ForegroundColor Yellow
    Write-Host "  $appxDir" -ForegroundColor Yellow
} else {
    $appxDir = $apRoot
}
if (-not (Test-Path $appxDir)) { New-Item -ItemType Directory -Path $appxDir -Force | Out-Null }

$publishArgs = @()
$argsStr = ($args -join ' ').Trim()
if ($argsStr -notmatch '(^|\s)-c(\s|$)') { $publishArgs += '-c', 'Release' }
if ($argsStr -notmatch 'Platform=') { $publishArgs += '-p:Platform=x64' }
$publishArgs += "-p:AppxPackageDir=$appxDir\"
$publishArgs += $args

& $scoopDotnet publish $proj @publishArgs
$code = $LASTEXITCODE

if ($code -eq 0) {
    $msix = Get-ChildItem $appxDir -Recurse -Filter *.msix -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($msix) {
        Write-Host ""
        Write-Host "MSIX 生成成功:" -ForegroundColor Green
        Write-Host "  $($msix.FullName)"
        Write-Host ""
        Write-Host "安装命令（侧载）：以管理员身份运行 .\install.ps1" -ForegroundColor Cyan
    } else {
        Write-Host "构建成功，但未找到 .msix（请检查 PublishAppxPackage 是否为 true）。" -ForegroundColor Yellow
    }
}
exit $code
