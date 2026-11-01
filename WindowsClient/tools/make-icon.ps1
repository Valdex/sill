# Makes WindowsClient/Sill.Windows/Sill.ico from design/AppIcon.svg: Microsoft Edge renders the SVG
# headless at 1024 px, then System.Drawing masks it to a rounded square (the macOS icon's 229/1024
# corner) and writes every size Windows asks for as a PNG entry of one .ico. Run from the repository
# root in Windows PowerShell or pwsh:  powershell -File WindowsClient/tools/make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$svg = Join-Path $root 'design\AppIcon.svg'
$out = Join-Path $root 'WindowsClient\Sill.Windows\Sill.ico'
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge is needed to render the SVG.' }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("sill-icon-" + [guid]::NewGuid())
New-Item -ItemType Directory $tmp | Out-Null
try {
    $html = Join-Path $tmp 'icon.html'
    $svgUrl = 'file:///' + ($svg -replace '\\', '/')
    Set-Content -Path $html -Encoding UTF8 -Value "<html><body style='margin:0;background:transparent'><img src='$svgUrl' width=1024 height=1024></body></html>"
    $png = Join-Path $tmp 'icon1024.png'
    & $edge --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 --window-size=1024,1024 "--screenshot=$png" ('file:///' + ($html -replace '\\', '/')) 2>$null
    for ($i = 0; $i -lt 50 -and -not (Test-Path $png); $i++) { Start-Sleep -Milliseconds 200 }
    Start-Sleep -Milliseconds 500
    $source = [Drawing.Image]::FromFile($png)

    $sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
    $entries = @()
    foreach ($s in $sizes) {
        $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
        $r = [single]($s * 229 / 1024); $d = 2 * $r
        $path = New-Object Drawing.Drawing2D.GraphicsPath
        $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($s - $d, 0, $d, $d, 270, 90)
        $path.AddArc($s - $d, $s - $d, $d, $d, 0, 90); $path.AddArc(0, $s - $d, $d, $d, 90, 90); $path.CloseFigure()
        $g.SetClip($path)
        $g.DrawImage($source, 0, 0, $s, $s)
        $g.Dispose()
        $ms = New-Object IO.MemoryStream
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
        $entries += , @($s, $ms.ToArray())
        $bmp.Dispose()
    }
    $source.Dispose()

    # ICONDIR, then one ICONDIRENTRY per size, then the PNGs.
    $f = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $f
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $s = $e[0]; $data = $e[1]
        $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
        $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
        $offset += $data.Length
    }
    foreach ($e in $entries) { $w.Write([byte[]]$e[1]) }
    $w.Flush()
    [IO.File]::WriteAllBytes($out, $f.ToArray())
    Write-Host "Wrote $out ($($f.Length) bytes, $($sizes -join ', ') px)"
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
