# Regenerates Rewake.ico from the SVG sources in this folder.
# Per-size artwork keeps shapes bold at taskbar/tray sizes:
#   Rewake.svg        -> 256/128/64/48  (full design)
#   Rewake-small.svg  -> 32/24/20       (simplified)
#   Rewake-16.svg     -> 16             (minimal)
# Requires Google Chrome. Afterwards: dotnet publish Rewake.csproj ...

$assets = Split-Path -Parent $MyInvocation.MyCommand.Path
$dst = Join-Path $assets 'Rewake.ico'
$work = Join-Path $env:TEMP 'Rewake-icon-build'
New-Item -ItemType Directory -Force -Path $work | Out-Null
Copy-Item (Join-Path $assets 'Rewake*.svg') $work -Force

$chrome = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
            "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chrome) { throw 'Google Chrome not found.' }

$plan = @(
    @{ Size = 256; Svg = 'Rewake.svg' },
    @{ Size = 128; Svg = 'Rewake.svg' },
    @{ Size = 64;  Svg = 'Rewake.svg' },
    @{ Size = 48;  Svg = 'Rewake.svg' },
    @{ Size = 32;  Svg = 'Rewake-small.svg' },
    @{ Size = 24;  Svg = 'Rewake-small.svg' },
    @{ Size = 20;  Svg = 'Rewake-small.svg' },
    @{ Size = 16;  Svg = 'Rewake-16.svg' }
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
Copy-Item "$work\ico-256.png" (Join-Path $assets 'Rewake-256.png') -Force

Remove-Item $work -Recurse -Force
"ICO regenerated: $dst ($([math]::Round((Get-Item $dst).Length / 1KB, 1)) KB, $($blobs.Count) sizes)"
