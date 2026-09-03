param(
    [Parameter(Mandatory=$true)][string]$In,
    [Parameter(Mandatory=$true)][string]$Out,
    [string]$Key = "UnitEmblemImage",
    [string]$HeaderComment = "Generated - do not edit by hand."
)

$svg = [System.IO.File]::ReadAllText($In, [System.Text.Encoding]::UTF8)

$q = [char]34

$clipRe = '<clipPath[^>]*>\s*<path[^>]*\s' + 'd=' + $q + '([^' + $q + ']+)' + $q
$clip = [regex]::Match($svg, $clipRe).Groups[1].Value.Trim()
if (-not $clip) { throw "clip path not found" }

$body = [regex]::Match($svg, '<g\s+clip-path[^>]*>([\s\S]*)</g>').Groups[1].Value
$paths = [regex]::Matches($body, '<path\b[^>]*>')

$fillRe = 'fill=' + $q + '([^' + $q + ']+)' + $q
$dRe = '\sd=' + $q + '([^' + $q + ']+)' + $q
$trRe = 'transform=' + $q + 'translate\(([-0-9.]+)\s+([-0-9.]+)\)' + $q

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('<!-- ' + $HeaderComment + ' -->')
$lines.Add('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"')
$lines.Add('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
$lines.Add('    <DrawingImage x:Key="' + $Key + '">')
$lines.Add('        <DrawingImage.Drawing>')
$lines.Add('            <DrawingGroup ClipGeometry="F1 ' + $clip + '">')

$count = 0
foreach ($m in $paths) {
    $tag = $m.Value
    $fill = [regex]::Match($tag, $fillRe).Groups[1].Value
    $d = [regex]::Match($tag, $dRe).Groups[1].Value.Trim()
    if (-not $d) { continue }
    if (-not $fill) { $fill = "#000000" }

    $tr = [regex]::Match($tag, $trRe)
    $tx = "0"; $ty = "0"
    if ($tr.Success) { $tx = $tr.Groups[1].Value; $ty = $tr.Groups[2].Value }

    $lines.Add('                <GeometryDrawing Brush="' + $fill + '">')
    $lines.Add('                    <GeometryDrawing.Geometry>')
    $lines.Add('                        <PathGeometry FillRule="Nonzero" Figures="' + $d + '">')
    if ($tx -ne "0" -or $ty -ne "0") {
        $lines.Add('                            <PathGeometry.Transform>')
        $lines.Add('                                <TranslateTransform X="' + $tx + '" Y="' + $ty + '"/>')
        $lines.Add('                            </PathGeometry.Transform>')
    }
    $lines.Add('                        </PathGeometry>')
    $lines.Add('                    </GeometryDrawing.Geometry>')
    $lines.Add('                </GeometryDrawing>')
    $count++
}

$lines.Add('            </DrawingGroup>')
$lines.Add('        </DrawingImage.Drawing>')
$lines.Add('    </DrawingImage>')
$lines.Add('</ResourceDictionary>')

$text = [string]::Join("`r`n", $lines) + "`r`n"
[System.IO.File]::WriteAllText($Out, $text, (New-Object System.Text.UTF8Encoding($true)))
Write-Output ("wrote {0} paths -> {1}" -f $count, $Out)
