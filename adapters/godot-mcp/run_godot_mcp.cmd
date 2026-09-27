@echo off
setlocal
set "ROOT=%~dp0"
if not exist "%ROOT%.venv\Scripts\python.exe" (
  echo Godot MCP venv is missing. Run setup.ps1 first. 1>&2
  exit /b 2
)
"%ROOT%.venv\Scripts\python.exe" -m godot_mcp.server
