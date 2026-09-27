param(
    [Parameter(Mandatory=$false)]
    [string]$ProjectRoot = "F:\My Lab\my-lab-4-exp"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Venv = Join-Path $Root ".venv"
$Python = Join-Path $Venv "Scripts\python.exe"

if (-not (Test-Path (Join-Path $ProjectRoot "project.godot"))) { throw "Not a Godot project: $ProjectRoot" }
if (-not (Test-Path $Python)) { py -3 -m venv $Venv }
& $Python -m pip install --upgrade pip
& $Python -m pip install -e $Root

$AppDir = Join-Path $env:APPDATA "ConstantineHub"
$ConfigPath = Join-Path $AppDir "godot-mcp.json"
New-Item -ItemType Directory -Force -Path $AppDir | Out-Null
if (-not (Test-Path $ConfigPath)) {
    $Config = [ordered]@{
        schema_version = 1
        project_root = (Resolve-Path $ProjectRoot).Path
        editor_host = "127.0.0.1"
        editor_port = 6262
        runtime_host = "127.0.0.1"
        runtime_port = 6263
        godot_executable = $null
        tools_c_root = "E:\MyCreations\Tools_C"
        max_file_bytes = 2000000
    }
    $Config | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $ConfigPath
}
Write-Host "Godot MCP installed."
Write-Host "Config: $ConfigPath"
Write-Host "Server command: $Root\run_godot_mcp.cmd"
Write-Host "Project addon source: $Root\project-addon\addons\constantine_mcp"
