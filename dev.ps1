param(
    [ValidateSet('start', 'restart', 'stop', 'close', 'status')]
    [string]$Action = 'start'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\Chickxel.App\Chickxel.App.csproj'
$exe = Join-Path $root 'src\Chickxel.App\bin\Debug\net10.0\Chickxel.App.exe'

function Get-DevProcesses {
    @(Get-Process -Name 'Chickxel.App' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
}

function Stop-DevProcesses {
    foreach ($process in Get-DevProcesses) {
        if (-not $process.CloseMainWindow()) {
            throw "PID $($process.Id): cannot close the window; close it manually and retry."
        }
        if (-not $process.WaitForExit(10000)) {
            throw "PID $($process.Id): still running (possibly waiting to save work); finish or cancel in the app and retry."
        }
        Write-Host "Stopped Chickxel PID $($process.Id)."
    }
}

try {
    switch ($Action) {
        'status' {
            $running = Get-DevProcesses
            if ($running.Count -eq 0) { Write-Host 'Local Debug Chickxel is not running.' }
            else { $running | ForEach-Object { Write-Host "Local Debug Chickxel PID $($_.Id) is running." } }
        }
        { $_ -in 'stop', 'close' } { Stop-DevProcesses }
        default {
            if ($Action -eq 'restart') { Stop-DevProcesses }
            elseif ((Get-DevProcesses).Count -gt 0) {
                Write-Host 'Local Debug Chickxel is already running. Use dev.bat restart to rebuild.'
                exit 0
            }

            & dotnet build $project -c Debug --nologo
            if ($LASTEXITCODE -ne 0) { throw 'Build failed; Chickxel was not started.' }
            $process = Start-Process -FilePath $exe -WorkingDirectory $root -PassThru
            if ($process.WaitForExit(1500)) { throw "Chickxel exited on startup (code $($process.ExitCode))." }
            Write-Host "Started local Debug Chickxel PID $($process.Id)."
        }
    }
} catch {
    Write-Error $_
    exit 1
}
