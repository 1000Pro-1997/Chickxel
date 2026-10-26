param(
    [ValidateSet('start', 'restart', 'stop', 'status')]
    [string]$Action = 'start'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\Gpixel.App\Gpixel.App.csproj'
$exe = Join-Path $root 'src\Gpixel.App\bin\Debug\net10.0\Gpixel.App.exe'

function Get-DevProcesses {
    @(Get-Process -Name 'Gpixel.App' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
}

function Stop-DevProcesses {
    foreach ($process in Get-DevProcesses) {
        if (-not $process.CloseMainWindow()) {
            throw "PID $($process.Id): cannot close the window; close it manually and retry."
        }
        if (-not $process.WaitForExit(10000)) {
            throw "PID $($process.Id): still running (possibly waiting to save work); finish or cancel in the app and retry."
        }
        Write-Host "Stopped Gpixel PID $($process.Id)."
    }
}

try {
    switch ($Action) {
        'status' {
            $running = Get-DevProcesses
            if ($running.Count -eq 0) { Write-Host 'Local Debug Gpixel is not running.' }
            else { $running | ForEach-Object { Write-Host "Local Debug Gpixel PID $($_.Id) is running." } }
        }
        'stop' { Stop-DevProcesses }
        default {
            if ($Action -eq 'restart') { Stop-DevProcesses }
            elseif ((Get-DevProcesses).Count -gt 0) {
                Write-Host 'Local Debug Gpixel is already running. Use dev.bat restart to rebuild.'
                exit 0
            }

            & dotnet build $project -c Debug --nologo
            if ($LASTEXITCODE -ne 0) { throw 'Build failed; Gpixel was not started.' }
            $process = Start-Process -FilePath $exe -WorkingDirectory $root -PassThru
            if ($process.WaitForExit(1500)) { throw "Gpixel exited on startup (code $($process.ExitCode))." }
            Write-Host "Started local Debug Gpixel PID $($process.Id)."
        }
    }
} catch {
    Write-Error $_
    exit 1
}
