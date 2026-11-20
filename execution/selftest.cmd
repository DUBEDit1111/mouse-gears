@echo off
rem Runs the built-in self-test. Your live mouse settings are put back at the end.
setlocal
cd /d "%~dp0.."
if not exist .tmp mkdir .tmp
start "" /wait dist\MouseGears.exe --selftest .tmp\selftest.txt
set RC=%errorlevel%
type .tmp\selftest.txt
exit /b %RC%
