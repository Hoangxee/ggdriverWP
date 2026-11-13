# make-assets.ps1 - generates the 21 WP8.1 tile/logo/splash PNGs of GDriveWP with System.Drawing.
#
#   powershell -ExecutionPolicy Bypass -File tools\make-assets.ps1 [-AssetsDir <dir>]
#
# Design:
#   * Logo / SmallLogo / Square71x71Logo / WideLogo / StoreLogo: white Drive triangle glyph on transparent (accent-coloured tile).
#   * SplashScreen: white background, the three-colour Drive triangle, app name "GG Drive" below it.
#   * BadgeLogo: white glyph on transparent (lock-screen badges must be white + alpha).
# This file is pure ASCII on purpose: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.
param(
    [string]$AssetsDir = (Join-Path $PSScriptRoot '..\GDriveWP\Assets'),
    [string]$FontsDir  = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$AssetsDir = [System.IO.Path]::GetFullPath($AssetsDir)
if (-not $FontsDir) { $FontsDir = Join-Path $AssetsDir 'Fonts' }
New-Item -ItemType Directory -Force $AssetsDir | Out-Null

$Primary  = [System.Drawing.Color]::FromArgb(255, 0x0B, 0x57, 0xD0)
$TextGray = [System.Drawing.Color]::FromArgb(255, 0x44, 0x47, 0x46)
$White    = [System.Drawing.Color]::White
$Green    = [System.Drawing.Color]::FromArgb(255, 0x00, 0xAC, 0x47)
$Yellow   = [System.Drawing.Color]::FromArgb(255, 0xFF, 0xBA, 0x00)
$Blue     = [System.Drawing.Color]::FromArgb(255, 0x26, 0x84, 0xFC)

# The three bands of the Drive logo in an 88 x 78 box
$bands = @(
    @{ Color = $Green;  Points = @(@(29,0), @(43,0), @(14,52), @(0,52)) },
    @{ Color = $Yellow; Points = @(@(43,0), @(58,0), @(87,52), @(73,52)) },
    @{ Color = $Blue;   Points = @(@(0,52), @(87,52), @(72,78), @(15,78)) }
)

$sizes = [ordered]@{
    'Logo'            = @{ 100 = '150x150'; 140 = '210x210';  240 = '360x360' }
    'SmallLogo'       = @{ 100 = '44x44';   140 = '62x62';    240 = '106x106' }
    'Square71x71Logo' = @{ 100 = '71x71';   140 = '99x99';    240 = '170x170' }
    'WideLogo'        = @{ 100 = '310x150'; 140 = '434x210';  240 = '744x360' }
    'StoreLogo'       = @{ 100 = '50x50';   140 = '70x70';    240 = '120x120' }
    'SplashScreen'    = @{ 100 = '480x800'; 140 = '672x1120'; 240 = '1152x1920' }
    'BadgeLogo'       = @{ 100 = '24x24';   140 = '33x33';    240 = '58x58' }
}
$glyphFraction = @{ 'Logo' = 0.46; 'SmallLogo' = 0.66; 'Square71x71Logo' = 0.6; 'WideLogo' = 0.46;
                    'StoreLogo' = 0.66; 'BadgeLogo' = 0.96 }

function Get-InstalledFamily([string[]]$names) {
    foreach ($n in $names) {
        try { return New-Object System.Drawing.FontFamily($n) } catch { }
    }
    return [System.Drawing.FontFamily]::GenericSansSerif
}

$pfc = New-Object System.Drawing.Text.PrivateFontCollection
$captionFamily = $null
$captionFont = Join-Path $FontsDir 'AppFont-Medium.ttf'
if (Test-Path $captionFont) {
    $pfc.AddFontFile($captionFont)
    if ($pfc.Families.Count -gt 0) { $captionFamily = $pfc.Families[0] }
}
if (-not $captionFamily) { $captionFamily = Get-InstalledFamily @('Segoe UI Semibold', 'Segoe UI', 'Arial') }

# Draws the triangle glyph scaled so that its WIDTH is $size, centred at ($cx, $cy); $mono = one colour for all bands
function Add-DriveGlyph([System.Drawing.Graphics]$g, [single]$cx, [single]$cy, [single]$size, $mono) {
    $s = $size / 88.0
    $w = 88.0 * $s; $h = 78.0 * $s
    $left = $cx - $w / 2; $top = $cy - $h / 2
    foreach ($b in $bands) {
        $pts = New-Object 'System.Drawing.PointF[]' 4
        for ($i = 0; $i -lt 4; $i++) {
            $pts[$i] = New-Object System.Drawing.PointF(($left + $b.Points[$i][0] * $s), ($top + $b.Points[$i][1] * $s))
        }
        $color = if ($mono) { $mono } else { $b.Color }
        $brush = New-Object System.Drawing.SolidBrush($color)
        $g.FillPolygon($brush, $pts)
        $brush.Dispose()
    }
}

function New-FittedTextPath([string]$text, [System.Drawing.FontFamily]$fam, [System.Drawing.RectangleF]$box) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddString($text, $fam, 0, 1000.0, (New-Object System.Drawing.PointF(0, 0)), [System.Drawing.StringFormat]::GenericTypographic)
    $b = $p.GetBounds()
    $sc = [Math]::Min($box.Width / $b.Width, $box.Height / $b.Height)
    $w = $b.Width * $sc; $h = $b.Height * $sc
    $x = $box.X + ($box.Width - $w) / 2; $y = $box.Y + ($box.Height - $h) / 2
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate($x, $y); $m.Scale($sc, $sc); $m.Translate(-$b.X, -$b.Y)
    $p.Transform($m); $m.Dispose()
    return $p
}

$written = 0
foreach ($name in $sizes.Keys) {
    foreach ($scale in 100, 140, 240) {
        $w, $h = $sizes[$name][$scale].Split('x') | ForEach-Object { [int]$_ }
        $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        if ($name -eq 'SplashScreen') {
            $g.Clear($White)
            $gs = [single]($w * 0.30)
            Add-DriveGlyph $g ([single]($w / 2)) ([single]($h * 0.42)) $gs $null
            $capBox = New-Object System.Drawing.RectangleF(0, ($h * 0.42 + $gs * 0.62), $w, ($w * 0.060))
            $cp = New-FittedTextPath 'GG Drive' $captionFamily $capBox
            $tb = New-Object System.Drawing.SolidBrush($TextGray)
            $g.FillPath($tb, $cp); $tb.Dispose(); $cp.Dispose()
        }
        elseif ($name -eq 'BadgeLogo') {
            $g.Clear([System.Drawing.Color]::Transparent)
            Add-DriveGlyph $g ([single]($w / 2)) ([single]($h / 2)) ([single]([Math]::Min($w, $h) * $glyphFraction[$name])) $White
        }
        else {
            # Transparent + white glyph: the phone paints the tile with the user's accent colour (manifest BackgroundColor="transparent")
            $g.Clear([System.Drawing.Color]::Transparent)
            Add-DriveGlyph $g ([single]($w / 2)) ([single]($h / 2)) ([single]([Math]::Min($w, $h) * $glyphFraction[$name])) $White
        }

        $g.Dispose()
        $out = Join-Path $AssetsDir ('{0}.scale-{1}.png' -f $name, $scale)
        $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $written++
        '{0,-30} {1}x{2}' -f (Split-Path $out -Leaf), $w, $h
    }
}
$pfc.Dispose()
Write-Host "$written PNGs written to $AssetsDir"
