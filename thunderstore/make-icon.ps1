# Draws thunderstore/icon.png (256x256, required by Thunderstore): the choppa from the side.
Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

function C($r, $gr, $b) { [System.Drawing.Color]::FromArgb(255, $r, $gr, $b) }
function Brush($c) { New-Object System.Drawing.SolidBrush $c }
$outline = New-Object System.Drawing.Pen (C 30 30 40), 5
$outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

$red = C 237 56 51; $yellow = C 255 204 38; $blue = C 51 140 242; $grey = C 90 90 102; $white = C 248 248 248; $black = C 15 15 15

# Sky circle
$sky = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 0, $size), (C 120 200 255), (C 200 240 255)
$g.FillEllipse($sky, 4, 4, 248, 248)

function Shape($fill, [scriptblock]$draw) { & $draw (Brush $fill) $outline }

# Tail boom, fin, tail rotor
Shape $red { param($b, $p) $g.FillRectangle($b, 22, 128, 100, 22); $g.DrawRectangle($p, 22, 128, 100, 22) }
Shape $yellow { param($b, $p)
    $pts = [System.Drawing.Point[]]@((New-Object System.Drawing.Point 18, 92), (New-Object System.Drawing.Point 42, 92), (New-Object System.Drawing.Point 52, 132), (New-Object System.Drawing.Point 22, 132))
    $g.FillPolygon($b, $pts); $g.DrawPolygon($p, $pts) }
Shape $red { param($b, $p) $g.FillEllipse($b, 8, 80, 22, 22); $g.DrawEllipse($p, 8, 80, 22, 22) }

# Skids
$skid = New-Object System.Drawing.Pen (C 30 30 40), 9
$skid.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $skid.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($skid, 92, 206, 212, 206)
$g.DrawLine($skid, 115, 186, 112, 206); $g.DrawLine($skid, 185, 186, 190, 206)

# Posts, roof, mast
$post = New-Object System.Drawing.Pen (C 30 30 40), 7
$g.DrawLine($post, 104, 80, 104, 120); $g.DrawLine($post, 200, 80, 200, 120)
Shape $yellow { param($b, $p) $g.FillRectangle($b, 92, 70, 120, 14); $g.DrawRectangle($p, 92, 70, 120, 14) }
Shape $grey { param($b, $p) $g.FillRectangle($b, 146, 52, 12, 20); $g.DrawRectangle($p, 146, 52, 12, 20) }

# Tub and nose
Shape $red { param($b, $p) $g.FillRectangle($b, 96, 116, 104, 72); $g.DrawRectangle($p, 96, 116, 104, 72) }
Shape $blue { param($b, $p) $g.FillRectangle($b, 118, 100, 40, 18); $g.DrawRectangle($p, 118, 100, 40, 18) }
Shape $yellow { param($b, $p) $g.FillEllipse($b, 168, 110, 76, 76); $g.DrawEllipse($p, 168, 110, 76, 76) }

# Googly eye
Shape $white { param($b, $p) $g.FillEllipse($b, 196, 122, 40, 40); $g.DrawEllipse($p, 196, 122, 40, 40) }
$g.FillEllipse((Brush $black), 214, 136, 18, 18)
$g.FillEllipse((Brush $white), 222, 139, 5, 5)

# Rotor blade with red tips
Shape $yellow { param($b, $p) $g.FillRectangle($b, 30, 40, 244 - 30, 14); $g.DrawRectangle($p, 30, 40, 244 - 30, 14) }
Shape $red { param($b, $p) $g.FillEllipse($b, 132, 34, 40, 26); $g.DrawEllipse($p, 132, 34, 40, 26) }
Shape $red { param($b, $p) $g.FillEllipse($b, 18, 36, 22, 22); $g.DrawEllipse($p, 18, 36, 22, 22) }
Shape $red { param($b, $p) $g.FillEllipse($b, 232, 36, 22, 22); $g.DrawEllipse($p, 232, 36, 22, 22) }

$out = Join-Path $PSScriptRoot 'icon.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "Wrote $out"
