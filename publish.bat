@echo off
chcp 65001 >nul
rem 배포 파일 만들기(Windows에서 실행): .NET 없이 실행되는 Gpixel.exe 하나 → dist\Gpixel-windows-x64.zip
rem macOS용 .app은 Mac에서 publish.sh로 만든다(또는 GitHub Actions 릴리스).
rem 사용: publish.bat [버전]   (기본 1.0.0)
setlocal
cd /d "%~dp0"
set VERSION=%~1
if "%VERSION%"=="" set VERSION=1.0.0

where dotnet >nul 2>nul || (echo .NET 10 SDK가 필요합니다: https://dotnet.microsoft.com/download & exit /b 1)

if exist dist rmdir /s /q dist
dotnet publish src\Gpixel.App\Gpixel.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:Version=%VERSION% -p:DebugType=none -p:DebugSymbols=false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist\.work\win-x64 || exit /b 1

mkdir dist\Gpixel-windows-x64
copy /y dist\.work\win-x64\Gpixel.App.exe dist\Gpixel-windows-x64\Gpixel.exe >nul
powershell -NoProfile -Command "Compress-Archive -Path 'dist\Gpixel-windows-x64' -DestinationPath 'dist\Gpixel-windows-x64.zip' -Force" || exit /b 1
rem 체크섬: 앱의 자동 업데이트가 내려받은 zip을 이것으로 검증한다.
powershell -NoProfile -Command "$h = (Get-FileHash -Algorithm SHA256 'dist\Gpixel-windows-x64.zip').Hash.ToLower(); Set-Content -Encoding ascii -Path 'dist\SHA256SUMS.txt' -Value ($h + '  Gpixel-windows-x64.zip')" || exit /b 1
rmdir /s /q dist\.work

echo.
echo 완료: dist\Gpixel-windows-x64\Gpixel.exe  (zip: dist\Gpixel-windows-x64.zip)
