@echo off
rem Builds dist\MouseGears.exe with the C# compiler built into Windows. No downloads needed.
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" ( echo .NET Framework 4 compiler not found & exit /b 1 )
if not exist dist mkdir dist
set "ICON="
if exist src\MouseGears.ico set "ICON=/win32icon:src\MouseGears.ico"
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu %ICON% /out:dist\MouseGears.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll src\MouseGears.cs
if errorlevel 1 exit /b 1
echo built dist\MouseGears.exe
