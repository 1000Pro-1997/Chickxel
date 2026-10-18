@echo off
rem Build and launch the Gpixel WinForms app.
cd /d "%~dp0"
dotnet run --project src\Gpixel.App\Gpixel.App.csproj -c Release %*
