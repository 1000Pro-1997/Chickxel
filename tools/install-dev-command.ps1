$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$bin = Join-Path $HOME '.local\bin'
$launcher = Join-Path $bin 'gpixel.cmd'
$marker = 'rem Managed by Gpixel tools/install-dev-command.ps1'
$body = "@echo off`r`n$marker`r`ncall `"$root\dev.bat`" %*`r`nexit /b %errorlevel%`r`n"

New-Item -ItemType Directory -Path $bin -Force | Out-Null
if (Test-Path $launcher) {
    $existing = Get-Content $launcher -Raw
    $previous = "@echo off`r`ncall `"$root\dev.bat`" %*`r`nexit /b %errorlevel%`r`n"
    if ($existing -ne $previous -and -not $existing.Contains($marker)) {
        throw "Not replacing existing command: $launcher"
    }
}
[System.IO.File]::WriteAllText($launcher, $body, [System.Text.UTF8Encoding]::new($false))

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$inPath = @($userPath -split ';' | Where-Object { $_.TrimEnd('\') -ieq $bin.TrimEnd('\') }).Count -gt 0
if (-not $inPath) {
    [Environment]::SetEnvironmentVariable('Path', ($userPath.TrimEnd(';') + ';' + $bin), 'User')
    Write-Host "Added $bin to user PATH. Open a new terminal to use gpixel."
}
Write-Host "Installed gpixel start/restart/stop/status in $launcher"
