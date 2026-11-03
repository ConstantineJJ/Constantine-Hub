@echo off
setlocal
set "ROOT=%~dp0"
rem Prefer this installed adapter over an old editable-install source mapping.
set "PYTHONPATH=%ROOT%src;%PYTHONPATH%"
if not exist "%ROOT%.venv\Scripts\python.exe" (
  echo Godot MCP venv is missing. Run setup.ps1 first. 1>&2
  exit /b 2
)
"%ROOT%.venv\Scripts\python.exe" -m godot_mcp.server
