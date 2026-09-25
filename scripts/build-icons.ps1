<#
.SYNOPSIS
    Draws the FocusDesk mark and writes the icon assets under Assets\.

.DESCRIPTION
    The mark is an open amber ring with a purple dot held clear of it: the ring is attention held
    on one thing, the gap in it faces the dot, and the dot is everything left outside. The two
    never touch, at any size, which is what keeps the shape readable in a 16-pixel tray slot.

    Three files come out, and every one of them is committed, so neither a build nor CI needs this
    script. Run it only when the mark itself changes.

      Assets\FocusDesk.ico        The application mark, 16/32/48/256. Wired as <ApplicationIcon>
                                  and as the installer's SetupIconFile.
      Assets\FocusDeskTray.ico    The notification-area icon for a dark taskbar, 16/20/24/28/32 -
                                  the slot sizes ZeroZero.Tray.TrayIconSlot reports from 100 % to
                                  200 % display scale.
      Assets\FocusDeskTrayLight.ico
                                  The same, in deepened hues that read on a light taskbar. A thin
                                  #d8a657 ring on white is about 2.2:1 against its background,
                                  which disappears at tray size.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-icons.ps1
#>

[CmdletBinding()]
param(
    # Where the .ico files land. Defaults to Assets\ beside this script's parent.
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets'
}
if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

# The mark's geometry, its colours and New-MarkBitmap, shared with build-wizard-images.ps1.
. (Join-Path $PSScriptRoot 'FocusDeskMark.ps1')

function ConvertTo-IcoFrame {
    <#
      .SYNOPSIS
        One bitmap as the bytes of an .ico frame, in the form that size of frame is read in.

      .DESCRIPTION
        A 256-pixel frame is PNG-compressed, which is the only practical form for it and the one
        Windows has read since Vista. Every smaller frame is a 32-bit device-independent bitmap
        with an empty mask beneath it, because that is the form GDI+ itself decodes: measured, a
        16-pixel frame written as PNG comes back as noise through System.Drawing.Icon, which is
        what the icon compiler, Inno Setup and the notification-icon library all reach for.
    #>
    param([Parameter(Mandatory)] [System.Drawing.Bitmap] $Bitmap)

    if ($Bitmap.Width -ge 256) {
        $stream = New-Object System.IO.MemoryStream
        try {
            $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return , $stream.ToArray()
        }
        finally { $stream.Dispose() }
    }

    $side = $Bitmap.Width
    $out = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($out)
    try {
        # BITMAPINFOHEADER. The height is doubled: the colour rows and the mask rows below them are
        # one image as far as the header is concerned.
        $writer.Write([uint32]40)
        $writer.Write([int32]$side)
        $writer.Write([int32]($side * 2))
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]0)     # uncompressed
        $writer.Write([uint32]0)     # image size, which may be left at zero
        $writer.Write([int32]0); $writer.Write([int32]0)
        $writer.Write([uint32]0); $writer.Write([uint32]0)

        # The colour rows, bottom upwards, as blue, green, red, alpha.
        for ($y = $side - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $side; $x++) {
                $p = $Bitmap.GetPixel($x, $y)
                $writer.Write([byte]$p.B)
                $writer.Write([byte]$p.G)
                $writer.Write([byte]$p.R)
                $writer.Write([byte]$p.A)
            }
        }

        # The mask, all zero: the alpha channel above is what decides what shows. Each row is
        # padded to four bytes.
        $maskRow = [Math]::Floor(($side + 31) / 32) * 4
        $writer.Write((New-Object byte[] ($maskRow * $side)))

        $writer.Flush()
        return , $out.ToArray()
    }
    finally { $writer.Dispose(); $out.Dispose() }
}

function Write-IcoFile {
    <#
      .SYNOPSIS
        Packs frames into one .ico. Each directory entry's size comes out of the frame itself, so
        an entry can never disagree with the picture behind it.
    #>
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [System.Collections.IEnumerable] $Frames
    )

    $frames = @($Frames)
    $stream = [System.IO.File]::Create($Path)
    try {
        $writer = New-Object System.IO.BinaryWriter($stream)

        # ICONDIR: reserved, type 1 (icon), frame count.
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)

        # Every frame's data follows the whole directory.
        $offset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $bytes = [byte[]]$frame

            # A PNG frame carries its size big-endian at byte 16 of its own header; a bitmap frame
            # carries it little-endian at byte 4, with the height doubled for the mask.
            if ($bytes[0] -eq 0x89 -and $bytes[1] -eq 0x50) {
                $width  = ($bytes[16] -shl 24) -bor ($bytes[17] -shl 16) -bor ($bytes[18] -shl 8) -bor $bytes[19]
                $height = ($bytes[20] -shl 24) -bor ($bytes[21] -shl 16) -bor ($bytes[22] -shl 8) -bor $bytes[23]
            }
            else {
                $width  = [System.BitConverter]::ToInt32($bytes, 4)
                $height = [System.BitConverter]::ToInt32($bytes, 8) / 2
            }

            # A side of 256 is written as 0: the field is one byte.
            $writer.Write([byte]($(if ($width  -ge 256) { 0 } else { $width })))
            $writer.Write([byte]($(if ($height -ge 256) { 0 } else { $height })))
            $writer.Write([byte]0)       # palette entries: none, the frame is full colour
            $writer.Write([byte]0)       # reserved
            $writer.Write([uint16]1)     # colour planes
            $writer.Write([uint16]32)    # bits per pixel
            $writer.Write([uint32]$bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $bytes.Length
        }

        # The cast is load-bearing: without it PowerShell binds the array to BinaryWriter's boolean
        # overload and writes a single byte per frame, leaving a directory pointing at nothing.
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
        $writer.Flush()
    }
    finally { $stream.Dispose() }
}

# --- The three files ------------------------------------------------------------------------

function Write-Mark {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int[]] $Sides,
        [Parameter(Mandatory)] [System.Drawing.Color] $RingColour,
        [Parameter(Mandatory)] [System.Drawing.Color] $DotColour
    )

    $frames = @()
    foreach ($side in $Sides) {
        $bitmap = New-MarkBitmap -Side $side -RingColour $RingColour -DotColour $DotColour
        try     { $frames += , (ConvertTo-IcoFrame -Bitmap $bitmap) }
        finally { $bitmap.Dispose() }
    }

    Write-IcoFile -Path $Path -Frames $frames
    Write-Host "Wrote $Path ($($Sides -join ', ') px)."
}

# The application mark. 256 is what Explorer's largest view draws.
Write-Mark -Path (Join-Path $OutputDirectory 'FocusDesk.ico') `
           -Sides 16, 32, 48, 256 -RingColour $AmberRing -DotColour $PurpleDot

# The tray slots: 16 at 100 %, 20 at 125 %, 24 at 150 %, 28 at 175 %, 32 at 200 %.
Write-Mark -Path (Join-Path $OutputDirectory 'FocusDeskTray.ico') `
           -Sides 16, 20, 24, 28, 32 -RingColour $AmberRing -DotColour $PurpleDot

Write-Mark -Path (Join-Path $OutputDirectory 'FocusDeskTrayLight.ico') `
           -Sides 16, 20, 24, 28, 32 -RingColour $AmberDeep -DotColour $PurpleDeep
