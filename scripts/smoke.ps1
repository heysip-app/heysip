<#
.SYNOPSIS
    Unpacks a release zip into an empty folder and checks that the app really starts, runs quietly and logs no errors.
.DESCRIPTION
    Run it on the zip that scripts/release.ps1 produced. It uses its own data folder and its own single-instance name,
    so it works even while another HeySip is running, and it never touches your real settings or drink history.
    Works on Windows PowerShell 5.1 and PowerShell 7. Exits with a non-zero code when a check fails.
.EXAMPLE
    ./scripts/smoke.ps1 -Zip artifacts/release/HeySip-0.1.0-win-x64.zip
#>
param(
    [Parameter(Mandatory = $true)][string]$Zip,
    [int]$WarmUpSeconds = 15,
    [int]$MeasureSeconds = 15
)

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]

function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "  ok    $what" } else { Write-Host "  FAIL  $what"; $failures.Add($what) }
}

$zipPath = (Resolve-Path $Zip).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("heysip-smoke-" + [guid]::NewGuid().ToString('N'))
$app = Join-Path $work 'app'
$data = Join-Path $work 'data'
New-Item -ItemType Directory -Path $app, $data | Out-Null

Write-Host "Unpacking $zipPath"
Expand-Archive -Path $zipPath -DestinationPath $app

$exe = Join-Path $app 'HeySip.exe'
Check (Test-Path $exe) 'HeySip.exe is in the zip'
Check (Test-Path (Join-Path $app 'LICENSE')) 'LICENSE is in the zip'
Check (Test-Path (Join-Path $app 'README.md')) 'README.md is in the zip'
Check (-not (Get-ChildItem $app -Recurse -Filter *.pdb)) 'no debug symbols (.pdb) are shipped'
Check (-not (Get-ChildItem $app -Recurse -Include *.Tests.dll, xunit*.dll)) 'no test assemblies are shipped'

$info = (Get-Item $exe).VersionInfo
Write-Host ("  info  product '{0}', version '{1}', file version {2}" -f $info.ProductName, $info.ProductVersion, $info.FileVersion)
Check ($info.ProductName -eq 'HeySip') 'the exe says it is HeySip'

$env:HEYSIP_DATA = $data
$env:HEYSIP_INSTANCE = 'HeySip.Smoke.' + [guid]::NewGuid().ToString('N')
$process = $null
try {
    Write-Host "Starting the app (warm-up $WarmUpSeconds s, then measuring $MeasureSeconds s)"
    $process = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds $WarmUpSeconds
    $process.Refresh()
    Check (-not $process.HasExited) 'the app is still running after start-up'

    if (-not $process.HasExited) {
        $before = $process.TotalProcessorTime
        Start-Sleep -Seconds $MeasureSeconds
        $process.Refresh()
        $cpu = ($process.TotalProcessorTime - $before).TotalSeconds / $MeasureSeconds * 100
        $memory = [math]::Round($process.WorkingSet64 / 1MB)
        Write-Host ("  info  steady CPU {0:N1}% of one core, memory {1} MB, {2} threads" -f $cpu, $memory, $process.Threads.Count)
        Check ($cpu -lt 15) 'idle CPU stays low (under 15% of one core with the window open)'
        Check ($memory -lt 400) 'memory stays reasonable (under 400 MB)'
    }

    # Closing the window must keep HeySip alive in the tray and nearly free to run (the default "close to tray").
    if (-not $process.HasExited) {
        [void]$process.CloseMainWindow()
        Start-Sleep -Seconds 5
        $process.Refresh()
        Check (-not $process.HasExited) 'closing the window keeps HeySip running in the tray'
        if (-not $process.HasExited) {
            $before = $process.TotalProcessorTime
            Start-Sleep -Seconds $MeasureSeconds
            $process.Refresh()
            $hiddenCpu = ($process.TotalProcessorTime - $before).TotalSeconds / $MeasureSeconds * 100
            Write-Host ("  info  hidden in the tray: CPU {0:N2}% of one core, memory {1} MB" -f $hiddenCpu, [math]::Round($process.WorkingSet64 / 1MB))
            Check ($hiddenCpu -lt 1) 'hidden in the tray it uses almost no CPU (under 1% of one core)'
        }
    }

    Check (Test-Path (Join-Path $data 'heysip.db')) 'the drink database was created'

    $logs = @(Get-ChildItem (Join-Path $data 'logs') -Filter *.log -ErrorAction SilentlyContinue)
    Check ($logs.Count -gt 0) 'a log file was written'
    $errors = @($logs | ForEach-Object { Select-String -Path $_.FullName -Pattern 'ERROR|exception' -SimpleMatch:$false })
    Check ($errors.Count -eq 0) 'the log has no errors'
    $errors | Select-Object -First 5 | ForEach-Object { Write-Host "        $($_.Line)" }
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Remove-Item Env:HEYSIP_DATA, Env:HEYSIP_INSTANCE -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Smoke test FAILED: $($failures.Count) check(s)."
    exit 1
}

Write-Host ""
Write-Host 'Smoke test passed.'
