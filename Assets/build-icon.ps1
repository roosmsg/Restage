# Regenerates Restage.ico from the SVG sources in this folder.
# Per-size artwork keeps shapes bold at taskbar/tray sizes:
#   Restage.svg        -> 256/128/64/48  (full design)
#   Restage-small.svg  -> 32/24/20       (simplified)
#   Restage-16.svg     -> 16             (minimal)
# Requires Google Chrome. Afterwards: dotnet publish Restage.csproj ...

$assets = Split-Path -Parent $MyInvocation.MyCommand.Path
$dst = Join-Path $assets 'Restage.ico'
$work = Join-Path $env:TEMP 'Restage-icon-build'
New-Item -ItemType Directory -Force -Path $work | Out-Null
Copy-Item (Join-Path $assets 'Restage*.svg') $work -Force

$chrome = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
            "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chrome) { throw 'Google Chrome not found.' }

$plan = @(
    @{ Size = 256; Svg = 'Restage.svg' },
    @{ Size = 128; Svg = 'Restage.svg' },
    @{ Size = 64;  Svg = 'Restage.svg' },
    @{ Size = 48;  Svg = 'Restage.svg' },
    @{ Size = 32;  Svg = 'Restage-small.svg' },
    @{ Size = 24;  Svg = 'Restage-small.svg' },
    @{ Size = 20;  Svg = 'Restage-small.svg' },
    @{ Size = 16;  Svg = 'Restage-16.svg' }
)

foreach ($p in $plan) {
    $s = $p.Size
    $html = "<!doctype html><html><head><style>html,body{margin:0;padding:0;background:transparent}</style></head><body><img src='$($p.Svg)' style='width:${s}px;height:${s}px;display:block'></body></html>"
    [IO.File]::WriteAllText("$work\render.html", $html)
    & $chrome --headless=new --disable-gpu --default-background-color=00000000 `
        --screenshot="$work\ico-$s.png" --window-size="$s,$s" --hide-scrollbars `
        "file:///$($work -replace '\\','/')/render.html" 2>$null | Out-Null
    Start-Sleep -Milliseconds 400
}

$blobs = New-Object System.Collections.ArrayList
foreach ($p in $plan) {
    [void]$blobs.Add(@{ Size = $p.Size; Data = [IO.File]::ReadAllBytes("$work\ico-$($p.Size).png") })
}
$fs = [IO.File]::Create($dst)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$blobs.Count)
$offset = 6 + 16 * $blobs.Count
foreach ($b in $blobs) {
    $dim = if ($b.Size -ge 256) { 0 } else { $b.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$b.Data.Length); $bw.Write([uint32]$offset)
    $offset += $b.Data.Length
}
foreach ($b in $blobs) { $bw.Write($b.Data) }
$bw.Dispose(); $fs.Dispose()

# Refresh the 256px PNG used as a WPF resource
Copy-Item "$work\ico-256.png" (Join-Path $assets 'Restage-256.png') -Force

Remove-Item $work -Recurse -Force
"ICO regenerated: $dst ($([math]::Round((Get-Item $dst).Length / 1KB, 1)) KB, $($blobs.Count) sizes)"
