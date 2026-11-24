param([switch]$InstallEditor)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$version = '6000.3.29f1'
$cli = Join-Path $env:LOCALAPPDATA 'Unity/bin/unity.exe'
if (-not (Test-Path -LiteralPath $cli)) {
    $installer = Join-Path $env:TEMP 'coop-unity-cli-install.ps1'
    Invoke-WebRequest 'https://unity.com/install.ps1' -OutFile $installer
    & $installer
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Unity CLI installation failed.' }
}
$env:PATH = (Split-Path $cli -Parent) + ';' + $env:PATH
function Invoke-Unity {
    & $cli @args
    if ($LASTEXITCODE -ne 0) { throw "Unity command failed: $($args -join ' ')" }
}
Invoke-Unity @('--version')
if ($InstallEditor) {
    $result = & $cli editors path $version --format json | ConvertFrom-Json
    if (-not $result.success) {
        # Interactive install asks the developer to accept the Editor terms.
        Invoke-Unity @('install', $version)
    }
}
if (Get-Command git -ErrorAction SilentlyContinue) {
    & git -C $projectRoot lfs install --local
    if ($LASTEXITCODE -ne 0) { throw 'Git LFS is required. Install Git for Windows with LFS.' }
    & git -C $projectRoot lfs pull
    if ($LASTEXITCODE -ne 0) { throw 'Git LFS asset download failed.' }
    & git -C $projectRoot config core.autocrlf false
    $editor = & $cli editors path $version --format json | ConvertFrom-Json
    if ($editor.success) {
        $mergeTool = Join-Path $editor.data.path 'Editor/Data/Tools/UnityYAMLMerge.exe'
        if (Test-Path -LiteralPath $mergeTool) {
            & git -C $projectRoot config merge.unityyamlmerge.name 'Unity Smart Merge'
            $mergeDriver = '"' + ($mergeTool -replace '\\','/') + '" merge -p %O %B %A %A'
            & git -C $projectRoot config merge.unityyamlmerge.driver $mergeDriver
        }
    }
}
if (Get-Command codex -ErrorAction SilentlyContinue) { Invoke-Unity @('setup','codex') }
if (-not (Get-Command claude -ErrorAction SilentlyContinue)) {
    $claudeRoot = Join-Path $env:LOCALAPPDATA 'Claude-Data'
    $bundled = Get-ChildItem -LiteralPath $claudeRoot -Filter claude.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'claude-code' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($bundled) { $env:PATH = $bundled.DirectoryName + ';' + $env:PATH }
}
if (Get-Command claude -ErrorAction SilentlyContinue) { Invoke-Unity @('setup','claude') }
Write-Host "Open this project with Unity $version in Unity Hub: $projectRoot"
Write-Host 'Use a separate clone and feature branch for each developer/AI agent.'
Write-Host 'Restart Codex/Claude after installing their Unity plugins.'
