param(
    [Parameter(Mandatory=$true)][string]$BuildLabel,
    [string]$OutputRoot = (Join-Path $env:TEMP "dmshot-history-measurements"),
    [ValidateRange(0,10000)][int]$WriterDelayMs = 0
)
$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$run = Join-Path $OutputRoot ((Get-Date -Format "yyyyMMdd-HHmmss") + "-" + ($BuildLabel -replace '[^a-zA-Z0-9_.-]', '_'))
New-Item -ItemType Directory -Path $run | Out-Null
$commit = git -C $repo rev-parse HEAD
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -ExpandProperty Name
@{
    label = $BuildLabel; commit = $commit; utc = (Get-Date).ToUniversalTime().ToString("O")
    os = $os.Caption; osBuild = $os.BuildNumber; cpu = $cpu
    ramGiB = [Math]::Round($os.TotalVisibleMemorySize / 1MB, 2)
    writerDelayMs = $WriterDelayMs; configuration = "Release"
} | ConvertTo-Json | Set-Content (Join-Path $run "build-and-device.json")
dotnet build (Join-Path $repo "windows/DMShot/DMShot.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
$oldPerf = $env:DMSHOT_HISTORY_PERF
$oldRoot = $env:DMSHOT_HISTORY_ROOT
$oldDelay = $env:DMSHOT_HISTORY_DELAY_MS
try {
    $env:DMSHOT_HISTORY_PERF = Join-Path $run "history.csv"
    $env:DMSHOT_HISTORY_ROOT = Join-Path $run "history"
    $env:DMSHOT_HISTORY_DELAY_MS = $WriterDelayMs.ToString()
    Write-Host "Close any existing DMShot instance first. Capture 10 seed entries, then follow docs/performance/windows-history.md. Quit from the tray to finish. Results: $run"
    $exe = Join-Path $repo "windows/DMShot/bin/Release/net8.0-windows10.0.22621.0/DMShot.exe"
    Start-Process -FilePath $exe -Wait
} finally {
    $env:DMSHOT_HISTORY_PERF = $oldPerf
    $env:DMSHOT_HISTORY_ROOT = $oldRoot
    $env:DMSHOT_HISTORY_DELAY_MS = $oldDelay
}
Write-Host "Recorded results in $run. No performance target is assumed from this run."
