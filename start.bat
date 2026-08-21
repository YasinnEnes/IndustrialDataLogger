@echo off
echo ========================================================
echo   Industrial IoT & Dijital Ikiz Platformu Baslatiliyor
echo ========================================================
echo.
cd /d "%~dp0"
echo Tarayici aciliyor: http://localhost:5000/index.html
start http://localhost:5000/index.html
echo Backend servisi baslatiliyor (Cikis icin Ctrl+C)...
dotnet run --project IndustrialDataLogger
pause
