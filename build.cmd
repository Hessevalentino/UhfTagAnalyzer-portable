@echo off
REM Sestaveni self-contained single-file x86 EXE.
REM Vysledek: publish\UhfTagAnalyzer.exe  (dvojklik, zadna instalace)
setlocal
set DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet publish -c Release -r win-x86 --self-contained true -o publish
echo.
echo Hotovo: %cd%\publish\UhfTagAnalyzer.exe
endlocal
