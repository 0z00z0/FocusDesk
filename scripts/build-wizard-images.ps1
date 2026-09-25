<#
.SYNOPSIS
    Draws the installer's wizard images and writes them under installer\wizard\.

.DESCRIPTION
    Two 24-bit bitmaps, both committed, so neither a build nor CI needs this script. Run it only
    when the artwork changes.

      installer\wizard\wizimg-492x942.bmp    WizardImageFile: the side banner on the Welcome and
                                             Finished pages. Dark studio ground; the studio mark,
                                             "ZeroZero Software" and its tagline above a divider,
                                             the FocusDesk mark and wordmark below it.
      installer\wizard\wizsmall-165x174.bmp  WizardSmallImageFile: the header of every inner page.
                                             The FocusDesk mark on white in its deepened hues, since
                                             the inner pages are Inno's light theme and a dark box
                                             would float on them.

    Each is one bitmap at 300 % of Inno Setup 6's image area (164 x 314 and 55 x 58 units). Inno
    loads the bitmap for the monitor Setup starts on and only stretches it after that, so a bitmap
    at the top of the range is only ever scaled down, which stays sharp; one at 100 % is scaled up
    on a high-DPI monitor and comes out soft. FocusDesk.iss sets WizardImageStretch=yes for the same
    reason.

    The FocusDesk mark comes from FocusDeskMark.ps1, the file the icons are drawn from. The studio
    mark follows the geometry of the shared About control's header. The brand typeface, Cascadia
    Mono, is read from the restored ZeroZero.Brand.WinUI package; Consolas stands in without it.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-wizard-images.ps1
#>

[CmdletBinding()]
param(
    # Where the bitmaps land. Defaults to installer\wizard\ beside this script's parent.
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'FocusDeskMark.ps1')

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'installer\wizard'
}
if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

# --- The studio palette ---------------------------------------------------------------------

$GroundColour     = [System.Drawing.ColorTranslator]::FromHtml('#0a0f17')
$GlowColour       = [System.Drawing.ColorTranslator]::FromHtml('#15263a')
$DividerColour    = [System.Drawing.ColorTranslator]::FromHtml('#1a2840')
$TextColour       = [System.Drawing.ColorTranslator]::FromHtml('#dde6f4')
$MutedColour      = [System.Drawing.ColorTranslator]::FromHtml('#64788f')
$StudioTeal       = [System.Drawing.ColorTranslator]::FromHtml('#27e0c8')
$StudioBlue       = [System.Drawing.ColorTranslator]::FromHtml('#11a9d6')
$StudioPurple     = [System.Drawing.ColorTranslator]::FromHtml('#7b8cff')
$StudioIndigo     = [System.Drawing.ColorTranslator]::FromHtml('#3f5be0')
$StudioZero       = [System.Drawing.ColorTranslator]::FromHtml('#d8a657')

# --- The brand typeface ---------------------------------------------------------------------

$packageRoot = $env:NUGET_PACKAGES
if (-not $packageRoot) { $packageRoot = Join-Path $env:USERPROFILE '.nuget\packages' }
$fontFile = Get-ChildItem -ErrorAction SilentlyContinue -Recurse -Filter 'CascadiaMono.ttf' `
    -Path (Join-Path $packageRoot 'zerozero.brand.winui') |
    Sort-Object FullName -Descending | Select-Object -First 1

$fonts = New-Object System.Drawing.Text.PrivateFontCollection
if ($fontFile) {
    $fonts.AddFontFile($fontFile.FullName)
    $BrandFamily = $fonts.Families[0]
}
else {
    Write-Warning 'CascadiaMono.ttf was not found in the NuGet cache; the text is drawn in Consolas.'
    $BrandFamily = New-Object System.Drawing.FontFamily('Consolas')
}

# --- Drawing helpers ------------------------------------------------------------------------

function New-Canvas {
    param([int] $Width, [int] $Height)
    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return $bitmap, $g
}

function New-RoundedRectPath {
    param([float] $X, [float] $Y, [float] $Width, [float] $Height, [float] $Radius)
    $d = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $Width - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $Width - $d, $Y + $Height - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $Height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Fill-Bar {
    param($G, [float] $X, [float] $Y, [float] $Width, [float] $Height, [System.Drawing.Color] $Colour)
    $brush = New-Object System.Drawing.SolidBrush($Colour)
    try { $G.FillRectangle($brush, $X, $Y, $Width, $Height) } finally { $brush.Dispose() }
}

function Draw-StudioMark {
    <#
      .SYNOPSIS
        The studio mark on its plate, on a 256-unit canvas at (X, Y), Scale pixels per unit.
    #>
    param($G, [float] $X, [float] $Y, [float] $Scale)

    $plate = New-RoundedRectPath ($X + 8 * $Scale) ($Y + 8 * $Scale) (240 * $Scale) (240 * $Scale) (52 * $Scale)
    try {
        $glow = New-Object System.Drawing.Drawing2D.PathGradientBrush($plate)
        try {
            $glow.CenterPoint    = New-Object System.Drawing.PointF(($X + 128 * $Scale), ($Y + 96 * $Scale))
            $glow.CenterColor    = $GlowColour
            $glow.SurroundColors = @($GroundColour)
            $G.FillPath($glow, $plate)
        }
        finally { $glow.Dispose() }
    }
    finally { $plate.Dispose() }

    # The two brackets, each a vertical gradient.
    $brackets = @(
        @{ Points = @(@(104, 78), @(82, 78), @(82, 178), @(104, 178));    Top = $StudioTeal;   Bottom = $StudioBlue },
        @{ Points = @(@(152, 78), @(174, 78), @(174, 178), @(152, 178)); Top = $StudioPurple; Bottom = $StudioIndigo }
    )
    foreach ($bracket in $brackets) {
        $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF(0, ($Y + 78 * $Scale))),
            (New-Object System.Drawing.PointF(0, ($Y + 178 * $Scale))), $bracket.Top, $bracket.Bottom)
        $pen = New-Object System.Drawing.Pen($gradient, (15 * $Scale))
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        try {
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
            $points = [System.Drawing.PointF[]]@($bracket.Points | ForEach-Object {
                New-Object System.Drawing.PointF(($X + $_[0] * $Scale), ($Y + $_[1] * $Scale)) })
            $path.AddLines($points)
            $G.DrawPath($pen, $path)
        }
        finally { $path.Dispose(); $pen.Dispose(); $gradient.Dispose() }
    }

    # The zero and its slash.
    $zero = New-RoundedRectPath ($X + 112 * $Scale) ($Y + 88 * $Scale) (32 * $Scale) (80 * $Scale) (12 * $Scale)
    $zeroPen = New-Object System.Drawing.Pen($StudioZero, (10 * $Scale))
    try { $G.DrawPath($zeroPen, $zero) } finally { $zeroPen.Dispose(); $zero.Dispose() }

    $slashPen = New-Object System.Drawing.Pen($StudioZero, (6 * $Scale))
    try {
        $slashPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $slashPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
        $G.DrawLine($slashPen, ($X + 148 * $Scale), ($Y + 92 * $Scale), ($X + 108 * $Scale), ($Y + 164 * $Scale))
    }
    finally { $slashPen.Dispose() }
}

function Draw-FocusDeskMark {
    <#
      .SYNOPSIS
        The FocusDesk mark in a square of Side pixels at (X, Y), drawn by the icons' own function.
    #>
    param($G, [float] $X, [float] $Y, [int] $Side, [System.Drawing.Color] $RingColour, [System.Drawing.Color] $DotColour)
    $mark = New-MarkBitmap -Side $Side -RingColour $RingColour -DotColour $DotColour
    try { $G.DrawImage($mark, $X, $Y, $Side, $Side) } finally { $mark.Dispose() }
}

function Draw-CentredText {
    param($G, [string] $Text, [float] $Size, [System.Drawing.FontStyle] $Style, [System.Drawing.Color] $Colour,
          [float] $Width, [float] $Top, [float] $Height)
    $font   = New-Object System.Drawing.Font($BrandFamily, $Size, $Style, [System.Drawing.GraphicsUnit]::Pixel)
    $brush  = New-Object System.Drawing.SolidBrush($Colour)
    $format = New-Object System.Drawing.StringFormat
    try {
        $format.Alignment     = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $G.DrawString($Text, $font, $brush, (New-Object System.Drawing.RectangleF(0, $Top, $Width, $Height)), $format)
    }
    finally { $format.Dispose(); $brush.Dispose(); $font.Dispose() }
}

# --- The side banner ------------------------------------------------------------------------
#
# Laid out in the 164 x 314 units of Inno's image area and scaled by k. Nothing overlaps: the
# studio block ends above the divider at 120 and the product block starts below it.

function Write-Banner {
    param([string] $Path, [int] $Width, [int] $Height)

    $bitmap, $g = New-Canvas $Width $Height
    try {
        [float] $k = $Width / 164.0

        # A glow near the top over the dark ground.
        $g.Clear($GroundColour)
        $area = New-Object System.Drawing.Drawing2D.GraphicsPath
        $area.AddRectangle((New-Object System.Drawing.RectangleF(0, 0, $Width, $Height)))
        $glow = New-Object System.Drawing.Drawing2D.PathGradientBrush($area)
        try {
            $glow.CenterPoint    = New-Object System.Drawing.PointF(($Width * 0.5), ($Height * 0.22))
            $glow.CenterColor    = $GlowColour
            $glow.SurroundColors = @($GroundColour)
            $glow.FocusScales    = New-Object System.Drawing.PointF(0.15, 0.05)
            $g.FillPath($glow, $area)
        }
        finally { $glow.Dispose(); $area.Dispose() }

        # Flat bars in the product's two hues: amber across the top, amber and purple along the foot.
        [float] $bar = [Math]::Max(2.0, 3 * $k)
        Fill-Bar $g 0 0 $Width $bar $AmberRing
        Fill-Bar $g 0 ($Height - $bar) ($Width / 2) $bar $AmberRing
        Fill-Bar $g ($Width / 2) ($Height - $bar) ($Width / 2) $bar $PurpleDot

        # The studio block: mark, name, tagline.
        $studioSide = 52 * $k
        Draw-StudioMark $g (($Width - $studioSide) / 2) (22 * $k) ($studioSide / 256.0)
        Draw-CentredText $g 'ZeroZero Software' (11 * $k) ([System.Drawing.FontStyle]::Regular) $TextColour $Width (78 * $k) (16 * $k)
        Draw-CentredText $g 'Small tools. Zero bloat.' (9 * $k) ([System.Drawing.FontStyle]::Regular) $MutedColour $Width (96 * $k) (14 * $k)

        $pen = New-Object System.Drawing.Pen($DividerColour, (1 * $k))
        try { $g.DrawLine($pen, (40 * $k), (120 * $k), ($Width - 40 * $k), (120 * $k)) } finally { $pen.Dispose() }

        # The product block: the mark, then the wordmark under it with a clear gap.
        [int] $markSide = [Math]::Round(92 * $k)
        Draw-FocusDeskMark $g (($Width - $markSide) / 2) (140 * $k) $markSide $AmberRing $PurpleDot
        Draw-CentredText $g 'FocusDesk' (16 * $k) ([System.Drawing.FontStyle]::Bold) $TextColour $Width (240 * $k) (28 * $k)

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally { $g.Dispose(); $bitmap.Dispose() }
    Write-Host "Wrote $Path ($Width x $Height px)."
}

# --- The inner-page header ------------------------------------------------------------------

function Write-Header {
    param([string] $Path, [int] $Width, [int] $Height)

    $bitmap, $g = New-Canvas $Width $Height
    try {
        [float] $k = $Width / 55.0
        $g.Clear([System.Drawing.Color]::White)
        [int] $side = [Math]::Round(46 * $k)
        Draw-FocusDeskMark $g (($Width - $side) / 2) (($Height - $side) / 2) $side $AmberDeep $PurpleDeep
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally { $g.Dispose(); $bitmap.Dispose() }
    Write-Host "Wrote $Path ($Width x $Height px)."
}

Write-Banner -Path (Join-Path $OutputDirectory 'wizimg-492x942.bmp')   -Width 492 -Height 942
Write-Header -Path (Join-Path $OutputDirectory 'wizsmall-165x174.bmp') -Width 165 -Height 174
