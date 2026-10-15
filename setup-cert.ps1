# setup-cert.ps1
# 为本扩展生成自签名证书（subject 必须与 src/Package.appxmanifest 的 Publisher 完全一致）。
#
# 证书要求（三条都是踩过坑之后定下来的）：
#   1) 必须有「代码签名 EKU」= 1.3.6.1.5.5.7.3.3 —— 否则 MSIX 打包报 APPX0107
#      (The certificate specified is not valid for signing)。
#   2) 绝不能带 BasicConstraints CA:TRUE —— CA 证书不能给 AppX 包签名，
#      带了 CA=1 时 MakeAppx 在签名阶段直接报 APPX0107 构建失败。
#   3) 因为不是 CA 证书，Windows 的「受信任的根」会拒绝导入（Root 要求 CA:TRUE），
#      侧载信任要装到「受信任人 Trusted People」—— 这正是微软官方给 MSIX 侧载的建议。
#
# 用法：.\setup-cert.ps1            （幂等，证书合格就跳过）
#       .\setup-cert.ps1 -Force    （强制重建）
param([switch]$Force)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$srcDir = Join-Path $root 'src'
$csproj = Join-Path $srcDir 'PowerEverything.csproj'
$pfxRel = 'PowerEverything_TemporaryKey.pfx'
$pfxPath = Join-Path $srcDir $pfxRel
$cerPath = Join-Path $srcDir 'PowerEverything_TemporaryKey.cer'
$password = 'PowerEverythingDev'
# 必须与 src/Package.appxmanifest 的 Publisher 完全一致
$subject = 'CN=PowerEverything.Dev'
$codeSigningEku = '1.3.6.1.5.5.7.3.3'

function CertIsSigningCapable($c) {
    $eku = $c.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' }
    if (-not $eku) { return $false }
    if (-not ($eku.Format($false) -match [regex]::Escape($codeSigningEku))) { return $false }
    foreach ($e in $c.Extensions) {
        if ($e -is [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension] -and $e.CertificateAuthority) {
            return $false
        }
    }
    return $true
}

function RemoveOldCerts($subj) {
    foreach ($store in @('My', 'TrustedPeople', 'Root')) {
        Get-ChildItem "Cert:\CurrentUser\$store" -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $subj } | ForEach-Object { Remove-Item $_.PSPath -Force -ErrorAction SilentlyContinue }
    }
    foreach ($store in @('TrustedPeople', 'Root')) {
        Get-ChildItem "Cert:\LocalMachine\$store" -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $subj } | ForEach-Object { Remove-Item $_.PSPath -Force -ErrorAction SilentlyContinue }
    }
}

# 1) 现有证书是否仍然合格
$cert = $null
if (-not $Force -and (Test-Path $pfxPath)) {
    [xml]$csprojXml = Get-Content $csproj
    $pg = $csprojXml.Project.PropertyGroup | Where-Object { $_.PackageCertificateThumbprint -ne $null }
    if ($pg -and $pg.PackageCertificateKeyFile -eq $pfxRel) {
        $thumb = $pg.PackageCertificateThumbprint
        $c = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $thumb } | Select-Object -First 1

        # ★ 重装系统 / 重置此电脑会把证书存储清空，但 D 盘里的 .pfx 还在。
        #   这时必须把【原证书】从 pfx 导回存储，绝不能重新生成一张：新证书指纹一变，
        #   之前已经打好、签好名的 MSIX 就全部作废（签名证书对不上，永远装不上），
        #   只能重新出包。恢复原证书则老包继续有效。
        if (-not $c) {
            Write-Host "证书存储里没有这张证书（重装系统会清空它），改从 pfx 恢复原证书……" -ForegroundColor Yellow
            $pw = ConvertTo-SecureString -String $password -Force -AsPlainText
            try {
                $c = Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\CurrentUser\My -Password $pw -ErrorAction Stop
                Write-Host "已从 pfx 恢复证书，指纹保持 $thumb 不变。" -ForegroundColor Green
            } catch {
                Write-Host "从 pfx 恢复失败（$($_.Exception.Message)），将重新生成一张新证书（已打好的包会失效）。" -ForegroundColor Yellow
                $c = $null
            }
        }

        if ($c -and (CertIsSigningCapable $c)) {
            $cert = $c
            Write-Host "证书已存在且可用于签名（有代码签名 EKU、非 CA），跳过生成。" -ForegroundColor Green
        }
    }
}

if (-not $cert) {
    RemoveOldCerts $subject

    Write-Host "生成自签名证书(代码签名 EKU, 非 CA): $subject" -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Type Custom -Subject $subject `
        -KeyUsage DigitalSignature -KeySpec Signature `
        -FriendlyName 'PowerEverything (dev)' `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(10) `
        -TextExtension @("2.5.29.37={text}$codeSigningEku")

    if (-not (CertIsSigningCapable $cert)) { Write-Error "生成的证书不可用于签名（缺少代码签名 EKU 或误带 CA:TRUE）。"; exit 1 }

    $secure = ConvertTo-SecureString -String $password -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $secure | Out-Null
    Write-Host "已导出 pfx: $pfxPath" -ForegroundColor Green
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
    Write-Host "已导出 cer: $cerPath" -ForegroundColor Green

    [xml]$csprojXml = Get-Content $csproj
    $pg = $csprojXml.Project.PropertyGroup | Where-Object { $_.PackageCertificateThumbprint -ne $null }
    if (-not $pg) { Write-Error "csproj 中找不到含 PackageCertificateThumbprint 的 PropertyGroup"; exit 1 }
    $pg.PackageCertificateThumbprint = $cert.Thumbprint
    if ($null -eq $pg.PackageCertificateKeyFile) {
        $el = $csprojXml.CreateElement('PackageCertificateKeyFile', $csprojXml.Project.NamespaceURI)
        $pg.AppendChild($el) | Out-Null
    }
    $pg.PackageCertificateKeyFile = $pfxRel
    if ($null -eq $pg.PackageCertificatePassword) {
        $el = $csprojXml.CreateElement('PackageCertificatePassword', $csprojXml.Project.NamespaceURI)
        $pg.AppendChild($el) | Out-Null
    }
    $pg.PackageCertificatePassword = $password
    $csprojXml.Save($csproj)
    Write-Host "已更新 csproj 证书指纹: $($cert.Thumbprint)" -ForegroundColor Green
} else {
    if (-not (Test-Path $cerPath)) { Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null }
}

# 2) 把 .cer 装进信任存储（侧载信任）。
#
#    ★ 只装「受信任人 Trusted People」，永远别往「受信任的根」里装：
#      这是代码签名证书、不是 CA，而 Root 存储要求 BasicConstraints CA:TRUE，
#      往 Root 装必定报「证书缺少基本约束扩展」—— 那是正常现象，不是需要修的故障。
#      侧载 MSIX 的正确落点就是 TrustedPeople（微软对 MSIX 侧载的官方建议）。
foreach ($store in @('Cert:\LocalMachine\TrustedPeople', 'Cert:\CurrentUser\TrustedPeople')) {
    try {
        Import-Certificate -FilePath $cerPath -CertStoreLocation $store -ErrorAction Stop | Out-Null
        Write-Host "已安装证书到 $store" -ForegroundColor Green
    } catch {
        Write-Host "跳过 $store（权限不足：本机受信任人需要管理员）" -ForegroundColor DarkGray
    }
}

# 3) 明确回报「这台机器现在到底信不信这张证书」—— 侧载失败几乎都卡在这一条。
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
if ($trusted) {
    Write-Host "本机已信任这张证书，可以直接侧载 MSIX。" -ForegroundColor Green
} else {
    Write-Host "⚠ 本机（LocalMachine\TrustedPeople）还没信任这张证书。" -ForegroundColor Yellow
    Write-Host "  请以【管理员身份】运行 .\install.ps1；否则侧载会报 0x800B0109（根证书必须是受信任的证书）。" -ForegroundColor Yellow
}

Write-Host "证书准备完成。" -ForegroundColor Cyan
