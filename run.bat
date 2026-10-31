@echo off
chcp 65001 >nul
rem 개발용: 소스에서 바로 빌드해 실행(Windows). .NET 없이 쓰려면 publish.bat로 만든 Chickxel.exe를 쓴다.
cd /d "%~dp0"
where dotnet >nul 2>nul || (echo .NET 10 SDK가 필요합니다: https://dotnet.microsoft.com/download & pause & exit /b 1)
dotnet run --project src\Chickxel.App\Chickxel.App.csproj -c Release -- %*
