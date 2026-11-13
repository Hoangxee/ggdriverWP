# build.ps1 - build + package GDriveWP (WP8.1 WinRT) into an unsigned .appx with MSBuild 14.
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1 [-Configuration Release] [-Platform ARM] [-Rebuild] [-Deploy]
param(
    [ValidateSet('Debug','Release')] [string]$Configuration = 'Release',
    [ValidateSet('ARM','x86')]       [string]$Platform      = 'ARM',
    [string]$Solution = '',
    [switch]$Rebuild,
    [switch]$Deploy      # ARM -> phone over USB (targetdevice:de), x86 -> emulator (targetdevice:xd)
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $Solution) { $Solution = Join-Path $root 'GDriveWP.sln' }
if (-not (Test-Path $Solution)) { throw "Solution not found: $Solution" }
$Solution = (Resolve-Path $Solution).Path
$slnDir = Split-Path -Parent $Solution

$msbuild   = "${env:ProgramFiles(x86)}\MSBuild\14.0\Bin\MSBuild.exe"
$appDeploy = "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows Phone\v8.1\Tools\AppDeploy\AppDeployCmd.exe"
if (-not (Test-Path $msbuild)) { throw "MSBuild 14.0 not found: $msbuild" }

# packages\ is git-ignored; seed it from a sibling repo (HintPath = ..\packages\...)
$pkgDir = Join-Path $slnDir 'packages'
$refPkgs = @('D:\lumia1020\YTMusicWP\packages', 'D:\lumia1020\GGTranslate\packages')
$needed = 'Newtonsoft.Json.13.0.4','System.ComponentModel.Primitives.4.3.0',
          'System.ComponentModel.TypeConverter.4.3.0','System.Runtime.InteropServices.RuntimeInformation.4.3.0'
foreach ($p in $needed) {
    if (Test-Path (Join-Path $pkgDir $p)) { continue }
    $src = $refPkgs | Where-Object { Test-Path (Join-Path $_ $p) } | Select-Object -First 1
    if (-not $src) { throw "Missing package $p (not in $pkgDir nor $($refPkgs -join ', '))" }
    # robocopy, not Copy-Item: RuntimeInformation has deep runtimes\ paths that break PS 5.1 Copy-Item past MAX_PATH
    robocopy (Join-Path $src $p) (Join-Path $pkgDir $p) /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $p (exit $LASTEXITCODE)" }
    Write-Host "Seeded packages\$p"
}

# Tile / splash PNGs are generated (git keeps them, but regenerate when missing)
if (-not (Test-Path (Join-Path $root 'GDriveWP\Assets\Logo.scale-240.png'))) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\make-assets.ps1')
    if ($LASTEXITCODE -ne 0) { throw "make-assets.ps1 failed" }
}

$target = if ($Rebuild) { 'Rebuild' } else { 'Build' }
& $msbuild $Solution "/t:$target" "/p:Configuration=$Configuration" "/p:Platform=$Platform" /m /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE" }

$suffix = if ($Configuration -eq 'Debug') { "_$($Platform)_Debug.appx" } else { "_$($Platform).appx" }
$appx = Get-ChildItem -Path "$slnDir\*\AppPackages\*_Test\*.appx" |
        Where-Object { $_.Name.EndsWith($suffix) } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $appx) { throw "Build succeeded but no *$suffix found under AppPackages" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($appx.FullName)
try {
    $names = @($zip.Entries | ForEach-Object { $_.FullName })
    $must = 'AppxManifest.xml','resources.pri','GDriveWP.exe','App.xbf','MainPage.xbf','Newtonsoft.Json.dll',
            'Assets/Fonts/AppFont-Regular.ttf','Assets/Fonts/AppFont-Medium.ttf','Assets/Fonts/OFL.txt'
    foreach ($m in $must) {
        if ($names -notcontains $m) { throw "$($appx.Name) is missing $m" }
    }
    $pngCount = @($names | Where-Object { $_ -like 'Assets/*.png' }).Count
    if ($pngCount -lt 21) { throw "$($appx.Name) has only $pngCount of 21 Assets/*.png" }
} finally { $zip.Dispose() }

Write-Host ("APPX: {0} ({1:N0} KB, {2} files)" -f $appx.FullName, ($appx.Length / 1KB), $names.Count)

if ($Deploy) {
    if (-not (Test-Path $appDeploy)) { throw "AppDeployCmd not found: $appDeploy" }
    $device = if ($Platform -eq 'x86') { 'xd' } else { 'de' }
    & $appDeploy /install $appx.FullName "/targetdevice:$device"
    if ($LASTEXITCODE -ne 0) {
        & $appDeploy /update $appx.FullName "/targetdevice:$device"
        if ($LASTEXITCODE -ne 0) { throw "AppDeployCmd failed with exit code $LASTEXITCODE" }
    }
    $productId = ([xml](Get-Content (Join-Path $root 'GDriveWP\Package.appxmanifest') -Encoding UTF8)).Package.PhoneIdentity.PhoneProductId
    & $appDeploy /launch $productId "/targetdevice:$device"
    if ($LASTEXITCODE -ne 0) { Write-Warning "Installed, but the app could not be launched: unlock the phone screen and open GG Drive." }
}
