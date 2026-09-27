@echo off
setlocal
set "HERE=%~dp0"
set "PYTHON=%HERE%.venv\Scripts\python.exe"

if not exist "%PYTHON%" (
  >&2 echo Local Files MCP virtual environment is missing.
  >&2 echo Run setup.ps1 first.
  exit /b 2
)

"%PYTHON%" -m local_files_mcp.server
exit /b %ERRORLEVEL%
