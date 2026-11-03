# Chickxel 설치(Windows PowerShell): irm https://raw.githubusercontent.com/1000Pro-1997/Chickxel/main/install.ps1 | iex
# 최신 릴리스 zip을 받아 %LOCALAPPDATA%\Programs\Chickxel\Chickxel.exe에 두고 시작 메뉴 바로 가기를 만든 뒤 실행한다.
# 브라우저로 받지 않아 "인터넷에서 받음" 표시가 붙지 않으므로 SmartScreen 창이 뜨지 않는다(Unblock-File로 한 번 더 지움).
# 이후 업데이트는 앱이 스스로 한다. 실행 중인 사용자 PowerShell의 설정을 바꾸지 않게 모두 스크립트 블록 안에서 한다.
& {
  $ErrorActionPreference = 'Stop'
  $ProgressPreference = 'SilentlyContinue'
  if (Get-Process Chickxel -ErrorAction SilentlyContinue) { throw 'Close Chickxel first, then run this command again.' }
  $url = 'https://github.com/1000Pro-1997/Chickxel/releases/latest/download/Chickxel-windows-x64.zip'
  $dir = Join-Path $env:LOCALAPPDATA 'Programs\Chickxel'
  $tmp = Join-Path ([IO.Path]::GetTempPath()) ('Chickxel-' + [guid]::NewGuid())
  New-Item -ItemType Directory -Force $tmp | Out-Null
  try {
    $zip = Join-Path $tmp 'Chickxel.zip'
    Write-Host 'Downloading Chickxel...'
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
    Expand-Archive $zip $tmp -Force
    New-Item -ItemType Directory -Force $dir | Out-Null
    Copy-Item (Join-Path $tmp 'Chickxel-windows-x64\Chickxel.exe') $dir -Force
  } finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
  }
  $exe = Join-Path $dir 'Chickxel.exe'
  Unblock-File $exe
  $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Chickxel.lnk'))
  $shortcut.TargetPath = $exe
  $shortcut.Save()
  Write-Host "Chickxel installed: $exe"
  Start-Process $exe
}
