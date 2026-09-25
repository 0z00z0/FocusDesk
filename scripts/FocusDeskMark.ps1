<#
.SYNOPSIS
    The FocusDesk mark: its geometry, its colours and one function that draws it. Dot-sourced by
    build-icons.ps1 and build-wizard-images.ps1, so the icons and the installer artwork draw the
    same shape.
#>

Add-Type -AssemblyName System.Drawing

# --- The mark -------------------------------------------------------------------------------
#
# Every measurement is a fraction of the icon's side, so the shape is identical at 16 and at 256.
# The clear space between the ring's outer edge and the dot works out at 0.076 of the side, which
# is a whole pixel at 16 and is what stops the two reading as one blob.

$RingCentre  = 0.40    # both axes
$RingRadius  = 0.30    # to the centre of the stroke
$RingStroke  = 0.095
$GapDegrees  = 72      # centred on the lower-right diagonal, facing the dot
$DotCentre   = 0.795   # both axes
$DotRadius   = 0.135

# The canonical mark. The application icon and the dark-taskbar tray icon both use it.
$AmberRing  = [System.Drawing.ColorTranslator]::FromHtml('#d8a657')
$PurpleDot  = [System.Drawing.ColorTranslator]::FromHtml('#7b8cff')

# The same two hues deepened for a light ground.
$AmberDeep  = [System.Drawing.ColorTranslator]::FromHtml('#9b7127')
$PurpleDeep = [System.Drawing.ColorTranslator]::FromHtml('#3f4fd1')

function New-MarkBitmap {
    <#
      .SYNOPSIS
        One frame of the mark, at a side in pixels. The caller disposes it.
    #>
    param(
        [Parameter(Mandatory)] [int] $Side,
        [Parameter(Mandatory)] [System.Drawing.Color] $RingColour,
        [Parameter(Mandatory)] [System.Drawing.Color] $DotColour
    )

    $bitmap = New-Object System.Drawing.Bitmap($Side, $Side, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $g = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.Clear([System.Drawing.Color]::Transparent)

            # The ring, drawn as an arc with a gap facing the dot. GDI+ measures from 3 o'clock and
            # sweeps clockwise with y growing downwards, so the lower-right diagonal is 45 degrees.
            $stroke = $RingStroke * $Side
            $radius = $RingRadius * $Side
            $cx     = $RingCentre * $Side
            $cy     = $RingCentre * $Side

            $pen = New-Object System.Drawing.Pen($RingColour, [float]$stroke)
            try {
                $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
                $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
                $g.DrawArc($pen,
                    [float]($cx - $radius), [float]($cy - $radius),
                    [float]($radius * 2),   [float]($radius * 2),
                    [float](45 + ($GapDegrees / 2)), [float](360 - $GapDegrees))
            }
            finally { $pen.Dispose() }

            # The dot, well outside the ring.
            $dr = $DotRadius * $Side
            $dc = $DotCentre * $Side
            $brush = New-Object System.Drawing.SolidBrush($DotColour)
            try {
                $g.FillEllipse($brush,
                    [float]($dc - $dr), [float]($dc - $dr),
                    [float]($dr * 2),   [float]($dr * 2))
            }
            finally { $brush.Dispose() }
        }
        finally { $g.Dispose() }
    }
    catch {
        $bitmap.Dispose()
        throw
    }

    return $bitmap
}
