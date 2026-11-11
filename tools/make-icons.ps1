# Draws the package logos and the .ico from scratch, so the repository needs no design tool.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\DockCalendar\Assets"
New-Item -ItemType Directory -Force $assets | Out-Null

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Logo([int]$size, [string]$file) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = "AntiAlias"
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 100.0
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 250, 250, 250))
    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 103, 192))
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 120, 130, 145))

    # Page, then the blue header clipped to the page's rounded top.
    $page = New-RoundedPath (12 * $s) (14 * $s) (76 * $s) (74 * $s) (14 * $s)
    $g.FillPath($white, $page)
    $g.SetClip($page)
    $g.FillRectangle($blue, 12 * $s, 14 * $s, 76 * $s, 22 * $s)
    $g.ResetClip()

    # A 4 x 3 grid of days with today picked out.
    for ($row = 0; $row -lt 3; $row++) {
        for ($col = 0; $col -lt 4; $col++) {
            $brush = if ($row -eq 1 -and $col -eq 2) { $blue } else { $dot }
            $g.FillEllipse($brush, (23 + $col * 15.5) * $s, (45 + $row * 13.5) * $s, 8 * $s, 8 * $s)
        }
    }

    $g.Dispose()
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

New-Logo 44 (Join-Path $assets "Square44x44Logo.png")
New-Logo 150 (Join-Path $assets "Square150x150Logo.png")
New-Logo 50 (Join-Path $assets "StoreLogo.png")

# An .ico is a small directory followed by PNG images.
$sizes = 16, 32, 48, 256
$images = foreach ($size in $sizes) {
    $temp = Join-Path $env:TEMP "dockcalendar-$size.png"
    New-Logo $size $temp
    $bytes = [System.IO.File]::ReadAllBytes($temp)
    Remove-Item $temp
    , $bytes
}

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets "DockCalendar.ico"), $stream.ToArray())

Write-Host "Icons written to $assets"
