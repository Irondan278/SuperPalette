<#
.SYNOPSIS
    Regenerates the package's image assets from branding\icon.png.

.DESCRIPTION
    Writes every logo the manifest names, at scales 100–400, plus the
    taskbar target sizes, into TaskImporterExtension\Assets. Run it after
    changing branding\icon.png and commit the results.

    The source should be a square PNG, 1024px or larger, with transparent
    corners. Uses System.Drawing, so it needs Windows and nothing else.
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot 'branding\icon.png'),
    [string]$Assets = (Join-Path $PSScriptRoot 'TaskImporterExtension\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$icon = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source))
if ($icon.Width -ne $icon.Height) { throw "Source must be square; it is $($icon.Width)x$($icon.Height)." }

# One bicubic pass from 1024px to 16px drops most of the pixels on the floor,
# so halve first and only then draw at the final size.
function Get-Scaled([System.Drawing.Bitmap]$image, [int]$size) {
    $current = $image
    while ($current.Width / 2 -ge $size) {
        $half = [int]($current.Width / 2)
        $next = New-Object System.Drawing.Bitmap $half, $half
        Write-Scaled $next $current 0 0 $half $half
        if ($current -ne $image) { $current.Dispose() }
        $current = $next
    }
    return $current
}

function Write-Scaled($target, $image, [int]$x, [int]$y, [int]$w, [int]$h) {
    $g = [System.Drawing.Graphics]::FromImage($target)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $attributes = New-Object System.Drawing.Imaging.ImageAttributes
    $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)  # no grey fringe at the edges
    $g.DrawImage($image, (New-Object System.Drawing.Rectangle $x, $y, $w, $h),
        0, 0, $image.Width, $image.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
    $g.Dispose()
}

# The icon centred on a transparent canvas, at a fraction of the canvas height.
function Save-Asset([string]$name, [int]$width, [int]$height, [double]$fill = 1.0) {
    $side = [int][Math]::Round([Math]::Min($width, $height) * $fill)
    $scaled = Get-Scaled $icon $side
    $canvas = New-Object System.Drawing.Bitmap $width, $height
    Write-Scaled $canvas $scaled ([int](($width - $side) / 2)) ([int](($height - $side) / 2)) $side $side
    $canvas.Save((Join-Path $Assets $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    if ($scaled -ne $icon) { $scaled.Dispose() }
}

$scales = [ordered]@{ 100 = 1.0; 125 = 1.25; 150 = 1.5; 200 = 2.0; 400 = 4.0 }

# name, base width, base height, how much of the height the icon fills.
# The leading commas stop PowerShell flattening the rows into one list.
$logos = @(
    , @('Square44x44Logo', 44, 44, 1.0)
    , @('SmallTile', 71, 71, 1.0)
    , @('Square150x150Logo', 150, 150, 1.0)
    , @('LargeTile', 310, 310, 1.0)
    , @('Wide310x150Logo', 310, 150, 0.66)
    , @('StoreLogo', 50, 50, 1.0)
    , @('SplashScreen', 620, 300, 0.5)
)

Get-ChildItem $Assets -Filter *.png | Remove-Item

foreach ($logo in $logos) {
    # Enumerate entries: on an ordered dictionary $scales[100] means position 100.
    foreach ($scale in $scales.GetEnumerator()) {
        # Away from zero, as Microsoft's size tables do: 71 at 150% is 107, not 106.
        $w = [int][Math]::Round($logo[1] * $scale.Value, [MidpointRounding]::AwayFromZero)
        $h = [int][Math]::Round($logo[2] * $scale.Value, [MidpointRounding]::AwayFromZero)
        Save-Asset "$($logo[0]).scale-$($scale.Key).png" $w $h $logo[3]
    }
}

# Taskbar, Start and Alt+Tab ask for exact pixel sizes. The icon carries its
# own plate, so the plated and unplated forms are the same image.
foreach ($size in 16, 24, 32, 48, 256) {
    foreach ($form in '', '_altform-unplated', '_altform-lightunplated') {
        Save-Asset "Square44x44Logo.targetsize-$size$form.png" $size $size
    }
}

# Loaded by relative path in the commands provider, which bypasses resource
# qualifiers, so it is a single plain file.
Save-Asset 'ExtensionIcon.png' 256 256

$icon.Dispose()
Write-Host "Wrote $((Get-ChildItem $Assets -Filter *.png).Count) images to $Assets"
