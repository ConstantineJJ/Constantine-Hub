param(
    [string]$ConfigPath = "$env:APPDATA\ConstantineHub\local-files-mcp.json"
)

$ErrorActionPreference = "Stop"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$Venv = Join-Path $Here ".venv"
$Python = Join-Path $Venv "Scripts\python.exe"
$ExampleConfig = Join-Path $Here "config.example.json"

Write-Host "Constantine Local Files MCP v0.1 setup"
Write-Host "Source: $Here"

if (-not (Get-Command py -ErrorAction SilentlyContinue)) {
    throw "Python launcher 'py' was not found. Install Python 3.10+ first."
}

if (-not (Test-Path $Python)) {
    Write-Host "Creating virtual environment..."
    & py -3 -m venv $Venv
}

Write-Host "Installing Local Files MCP..."
& $Python -m pip install --upgrade pip
& $Python -m pip install -e $Here

$ConfigDir = Split-Path -Parent $ConfigPath
New-Item -ItemType Directory -Force -Path $ConfigDir | Out-Null

if (-not (Test-Path $ConfigPath)) {
    Copy-Item $ExampleConfig $ConfigPath
    Write-Host "Created config from template: $ConfigPath"
    Write-Warning "Review allowed_roots before starting the MCP server."
} else {
    Write-Host "Config already exists; preserved: $ConfigPath"
}

Write-Host ""
Write-Host "Setup complete."
Write-Host "1. Edit: $ConfigPath"
Write-Host "2. Test server command: $Here\run_local_files_mcp.cmd"
Write-Host "3. Create an OpenAI tunnel and initialize a tunnel-client stdio profile using that command."
