<#
.SYNOPSIS
    配布用のインストーラーを作る（ADR-0027 / ADR-0031 / ADR-0035）。

.DESCRIPTION
    publish → vpk pack → Inno の 3 段。

    **これはリリース用ではない。** リリースの成果物は GitHub Actions でしか作らない
    （ADR-0031 決定 1）。このスクリプトは手元での動作確認のためだけに使う。

.EXAMPLE
    ./scripts/build-installer.ps1
    ./scripts/build-installer.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string] $Version = '0.1.0',
    [string] $OutputDir = 'release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

# --- 道具を探す ---------------------------------------------------------

$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
    $candidate = Join-Path $env:USERPROFILE '.dotnet\tools\vpk.exe'
    if (Test-Path $candidate) {
        $vpk = $candidate
    } else {
        throw "vpk が無い。`dotnet tool install -g vpk` で入れること（ADR-0027）"
    }
} else {
    $vpk = $vpk.Source
}

# よくある 3 箇所を見る（winget / 32bit 既定 / 64bit）。
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup が無い。`winget install JRSoftware.InnoSetup` で入れること（ADR-0035）"
}

Write-Host "vpk  : $vpk"
Write-Host "ISCC : $iscc"
Write-Host "版   : $Version"
Write-Host ''

# --- 1. 発行 ------------------------------------------------------------

Remove-Item -Recurse -Force publish, $OutputDir -ErrorAction SilentlyContinue

Write-Host '--- publish ---'
dotnet publish src/NutLens.App/NutLens.App.csproj `
    -c Release -r win-x64 --self-contained false `
    -o publish -p:Version=$Version --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish に失敗' }

# --- 2. Velopack で梱包 -------------------------------------------------

# build/Distribution.props だけが owner/repo/packId を持つ（ADR-0031 決定 7）。
[xml] $dist = Get-Content 'build/Distribution.props'
$packId = $dist.Project.PropertyGroup.VelopackPackageId
$packTitle = $dist.Project.PropertyGroup.VelopackPackageTitle

Write-Host ''
Write-Host "--- vpk pack（$packId） ---"

# **--runtime を省くと x86 として梱包される**（ADR-0027 実装で分かったこと 4）。
# --framework はランタイムが無い PC での導入を促すために要る（同 5）。
& $vpk pack `
    --packId $packId --packTitle $packTitle --packVersion $Version `
    --packDir publish --mainExe NutLens.exe --outputDir $OutputDir `
    --runtime win-x64 --framework net10.0-x64-desktop
if ($LASTEXITCODE -ne 0) { throw 'vpk pack に失敗' }

# --- 3. Inno で前段を包む -----------------------------------------------

$velopackSetup = Join-Path $repo "$OutputDir\$packId-win-Setup.exe"
if (-not (Test-Path $velopackSetup)) { throw "Velopack の Setup.exe が無い: $velopackSetup" }

Write-Host ''
Write-Host '--- ISCC ---'
& $iscc /Q `
    "/DAppVersion=$Version" `
    "/DVelopackSetup=$velopackSetup" `
    "/DOutputDir=$(Join-Path $repo $OutputDir)" `
    installer/NutLens.iss
if ($LASTEXITCODE -ne 0) { throw 'ISCC に失敗' }

# **Velopack の Setup.exe は配らない**（ADR-0035 決定 5）。
# 「どっちを実行すればいいのか」を作らないため、Inno に包んだら消す。
Remove-Item $velopackSetup -Force

Write-Host ''
Write-Host '--- できたもの ---'
Get-ChildItem $OutputDir | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } } | Format-Table -AutoSize
