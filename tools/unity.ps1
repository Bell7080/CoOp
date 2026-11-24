$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$unityCommand = Get-Command unity -ErrorAction SilentlyContinue
$unityCli = if ($unityCommand) { $unityCommand.Source } else { Join-Path $env:LOCALAPPDATA 'Unity/bin/unity.exe' }
if (-not (Test-Path -LiteralPath $unityCli)) { throw 'Unity CLI is missing. Run tools/setup-dev.ps1 first.' }
$previousProject = $env:UNITY_PROJECT_PATH
$exitCode = 1
Push-Location -LiteralPath $projectRoot
try {
    $env:UNITY_PROJECT_PATH = $projectRoot
    & $unityCli @args
    $exitCode = $LASTEXITCODE
} finally {
    $env:UNITY_PROJECT_PATH = $previousProject
    Pop-Location
}
exit $exitCode
