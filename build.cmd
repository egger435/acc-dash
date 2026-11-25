@echo off
rem ---------------------------------------------------------------------------
rem  ACC Telemetry Dashboard 1.5 - build script
rem  Compiles every .cs file under src\ with the .NET Framework C# compiler.
rem  Nothing else is needed: no SDK, no NuGet packages, no references to add.
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

tasklist /fi "imagename eq AccDash.exe" 2>nul | find /i "AccDash.exe" >nul
if not errorlevel 1 (
    echo AccDash.exe is running - close the dashboard window, then build again.
    exit /b 1
)

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Could not find csc.exe ^(.NET Framework 4^) on this machine.
    exit /b 1
)

rem csc does not expand wildcards across folders, so collect the sources first
if exist sources.rsp del sources.rsp
for /r "%~dp0src" %%f in (*.cs) do echo "%%f">>sources.rsp

rem application icon (the window also picks it up at runtime)
set ICON=/win32icon:"%~dp0assets\app.ico"
if not exist "%~dp0assets\app.ico" set ICON=

echo Building AccDash.exe ...
"%CSC%" /nologo /target:winexe /out:AccDash.exe /optimize+ %ICON% ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    @sources.rsp

if errorlevel 1 (
    echo.
    echo Build FAILED.
    exit /b 1
)

del sources.rsp
echo Build OK.
exit /b 0
