@echo off
rem ConHub Claudius channel launcher. Claude Desktop starts this; ConHub only toggles the switch.
setlocal
set "PYTHONUNBUFFERED=1"
set "PYTHONIOENCODING=utf-8"
where py >nul 2>nul
if %errorlevel%==0 (
  py -3 "%~dp0claudius_gateway.py" %*
) else (
  python "%~dp0claudius_gateway.py" %*
)
exit /b %errorlevel%
