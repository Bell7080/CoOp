<#
ElecMole end-to-end test.

Runs the DeveloperHostClient Play Mode scenario: the main Editor is the host and the "Player 2" clone Editor is a
client in a separate process, connected over Relay. Each step calls CoOpHorror.Testing.ElecMoleE2E in one of the
processes; the step drives that process's own player through the real input paths (walking NavMesh routes,
aiming, clicks, holds, skill checks). The runner asserts on the replicated state and saves screenshots.

Flow: room + join -> ship door -> CCTV view -> nose drinks the kettle (its crackle draws a monster placed nearby) -> tank pumped into the ship's
transmitter -> lamp drained and relit -> CCTV drained -> (host tops the transmitter up) ->
microwave -> transmitter full (surplus to money) -> death, ghost spectating, revive -> body carried aboard -> launch (transmitter fires, blackout) -> escape.

Usage (PowerShell, project root, with the Editor open and idle):
  .\tools\e2e-elecmole.ps1
  .\tools\e2e-elecmole.ps1 -KeepPlaying     # leave the scenario running afterwards
Results: Logs/E2E/<time>/report.md and screenshots. Exit code 0 only when every step passed.
#>
param(
    [string]$Scenario = 'Assets/Settings/PlayMode/DeveloperHostClient.asset',
    [string]$Client = 'Player 2',
    [switch]$KeepPlaying
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cliCommand = Get-Command unity -ErrorAction SilentlyContinue
$cli = if ($cliCommand) { $cliCommand.Source } else { Join-Path $env:LOCALAPPDATA 'Unity/bin/unity.exe' }
if (-not (Test-Path -LiteralPath $cli)) { throw 'Unity CLI is missing. Run tools/setup-dev.ps1 first.' }
$env:UNITY_PROJECT_PATH = $root
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path $root "Logs/E2E/$stamp"
$evalDir = Join-Path $out 'eval'
New-Item -ItemType Directory -Force -Path $evalDir | Out-Null
$results = New-Object System.Collections.Generic.List[object]
$script:failed = $false
$script:evalCount = 0

function Invoke-Cli([string[]]$CliArgs, [string]$Instance) {
    $all = @('command')
    if ($Instance) { $all += @('--instance', $Instance) }
    $all += $CliArgs
    $text = & $cli @all 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "unity $($all -join ' ') failed: $text" }
    return $text.Trim()
}

# Runs C# in one process and returns its string result.
function Invoke-Eval([string]$Code, [string]$Instance) {
    $script:evalCount++
    $file = Join-Path $evalDir ("e{0:000}.cs" -f $script:evalCount)
    [IO.File]::WriteAllText($file, $Code, (New-Object System.Text.UTF8Encoding($false)))
    $all = @('command')
    if ($Instance) { $all += @('--instance', $Instance) }
    $all += @('script', 'eval', '--result-only', '--timeout', '60', '--', '--file', $file)
    $text = & $cli @all 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "eval failed ($Instance): $text" }
    try { return [string]($text.Trim() | ConvertFrom-Json) } catch { return $text.Trim() }
}

function Quote([string]$s) { return '"' + $s.Replace('\', '\\').Replace('"', '\"') + '"' }

function Write-Step([string]$Name, [string]$Status, [string]$Detail, [double]$Seconds) {
    $results.Add([pscustomobject]@{ Step = $Name; Status = $Status; Seconds = [math]::Round($Seconds, 1); Detail = $Detail })
    $color = if ($Status -eq 'ok') { 'Green' } else { 'Red' }
    Write-Host ("[{0}] {1} ({2:0.0}s) {3}" -f $Status.ToUpper(), $Name, $Seconds, $Detail) -ForegroundColor $color
    if ($Status -ne 'ok') { $script:failed = $true }
}

# Runs one ElecMoleE2E step in a process and waits for it.
function Step([string]$Who, [string]$Name, [string]$Arg = '', [int]$Timeout = 150) {
    if ($script:failed) { Write-Step "$Who $Name $Arg" 'skipped' 'an earlier step failed' 0; return $false }
    $instance = if ($Who -eq 'client') { $Client } else { $null }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $started = Invoke-Eval ("return CoOpHorror.Testing.ElecMoleE2E.Begin({0}, {1});" -f (Quote $Name), (Quote $Arg)) $instance
    if ($started -notlike 'started*') { Write-Step "$Who $Name $Arg" 'fail' $started $sw.Elapsed.TotalSeconds; return $false }
    while ($true) {
        Start-Sleep -Milliseconds 700
        $poll = Invoke-Eval 'return CoOpHorror.Testing.ElecMoleE2E.Poll();' $instance
        $parts = $poll.Split('|')
        if ($parts[0] -eq 'ok') { Write-Step "$Who $Name $Arg" 'ok' $parts[3] $sw.Elapsed.TotalSeconds; return $true }
        if ($parts[0] -eq 'fail') { Write-Step "$Who $Name $Arg" 'fail' $parts[3] $sw.Elapsed.TotalSeconds; return $false }
        if ($sw.Elapsed.TotalSeconds -gt $Timeout) { Write-Step "$Who $Name $Arg" 'fail' "runner timeout ($poll)" $sw.Elapsed.TotalSeconds; return $false }
    }
}

function Snapshot([string]$Who = 'host') {
    $instance = if ($Who -eq 'client') { $Client } else { $null }
    return Invoke-Eval 'return CoOpHorror.Testing.ElecMoleE2E.Snapshot();' $instance
}

# Asserts on a host snapshot: every pattern must match.
function Check([string]$Name, [string[]]$Patterns, [string]$Who = 'host') {
    if ($script:failed) { Write-Step "check $Name" 'skipped' 'an earlier step failed' 0; return }
    $snap = Snapshot $Who
    $missing = @($Patterns | Where-Object { $snap -notmatch $_ })
    if ($missing.Count -eq 0) { Write-Step "check $Name" 'ok' $snap 0 }
    else { Write-Step "check $Name" 'fail' ("missing: " + ($missing -join ', ') + " | " + $snap) 0 }
}

function Shot([string]$Name, [string]$Who = 'host') {
    $file = Join-Path $out "$Name.png"
    $instance = if ($Who -eq 'client') { $Client } else { $null }
    try {
        Invoke-Cli @('capture', 'game_view', '--', '--output-path', $file) $instance | Out-Null
        Write-Host "  screenshot $Name.png"
    } catch { Write-Host "  screenshot $Name failed: $_" -ForegroundColor Yellow }
}

function Wait-Until([scriptblock]$Condition, [int]$Seconds, [string]$What) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 800
    }
    Write-Step "wait $What" 'fail' "timeout after $Seconds s" $sw.Elapsed.TotalSeconds
    return $false
}

$totalWatch = [Diagnostics.Stopwatch]::StartNew()
try {
    # ---------- scenario and room ----------
    $status = Invoke-Cli @('editor.playmode', 'get_scenario_status', '--result-only') | ConvertFrom-Json
    if ($status.playMode -ne 'stopped') {
        Write-Host 'Stopping the running play session first.'
        Invoke-Cli @('editor.playmode', 'stop_scenario') | Out-Null
        Invoke-Cli @('editor.playmode', 'stop') | Out-Null
        Start-Sleep -Seconds 4
    }
    Invoke-Cli @('editor.playmode', 'set_active_scenario', '--', '--path', $Scenario) | Out-Null
    Invoke-Cli @('editor.playmode', 'start_scenario') | Out-Null
    $ready = Wait-Until {
        try {
            $s = Invoke-Cli @('editor.playmode', 'get_scenario_status', '--result-only') | ConvertFrom-Json
            $p2 = $s.players | Where-Object { $_.name -eq $Client }
            return ($s.playMode -eq 'playing' -and $p2 -and $p2.reachable)
        } catch { return $false }
    } 240 'scenario players'
    if (-not $ready) { throw 'Scenario did not start.' }
    Start-Sleep -Seconds 3
    $clientPlaying = Wait-Until { (Invoke-Eval 'return UnityEngine.Application.isPlaying.ToString();' $Client) -eq 'True' } 120 'client play mode'
    if (-not $clientPlaying) { throw 'Client did not enter play mode.' }

    Invoke-Eval 'return CoOpHorror.Testing.ElecMoleE2E.HostRoom();' | Out-Null
    $code = ''
    $hosted = Wait-Until {
        $r = Invoke-Eval 'return CoOpHorror.Testing.ElecMoleE2E.RoomStatus();'
        if ($r -match 'code=([A-Z0-9]+)\|connected=True') { $script:roomCode = $Matches[1]; return $true }
        return $false
    } 60 'host room'
    if (-not $hosted) { throw 'Host could not create a room.' }
    $code = $script:roomCode
    Write-Step 'host room' 'ok' "code $code" 0
    Invoke-Eval ("return CoOpHorror.Testing.ElecMoleE2E.JoinRoom({0});" -f (Quote $code)) $Client | Out-Null
    $joined = Wait-Until { (Invoke-Eval 'return CoOpHorror.Testing.ElecMoleE2E.RoomStatus();') -match 'players=2' } 60 'client join'
    if (-not $joined) { throw 'Client could not join.' }
    Start-Sleep -Seconds 2
    Write-Step 'client join' 'ok' (Invoke-Eval 'return CoOpHorror.Testing.ElecMoleE2E.RoomStatus();' $Client) 0

    # ---------- gameplay ----------
    Step host setup | Out-Null
    Step client setup | Out-Null
    Check 'start state' @('money=0', 'stage=Exploring', 'door=closed', 'tx=Charging', 'stored=300/2500', 'p0=hp100', 'p1=hp100', 'cctv=\d+,\d+;')

    Step host door | Out-Null
    Check 'door open' @('door=open')
    Shot '02-host-door-open'

    # Client goes out; the host watches the CCTV.
    Step host cctv-live 1 | Out-Null
    Step client power-cctv 1 | Out-Null
    Step host cctv-view 1 | Out-Null
    Shot '03-host-cctv-view'
    Check 'cctv view' @('cam1=on', 'cctvView=True', 'map[1-9]')
    Step host cctv-exit | Out-Null

    Step host lure-setup 'Kettle' | Out-Null
    Step client suck 'Kettle' 240 | Out-Null
    Check 'tank filled from the kettle' @('kettle=0/1500W', 'p1=[^;]*tank1500')
    Check 'draining the kettle drew the monster' @('monster=investigating', 'lure=[^;]*kettle')
    Step host lure-reset | Out-Null
    Shot '04-client-kettle' 'client'
    Step client inject 180 | Out-Null
    Check 'tank pumped into the transmitter' @('tx=Charging', 'stored=1(799|800)/2500', 'p1=[^;]*tank0')
    Step client drain-lamp | Out-Null
    Check 'lamp drained' @('drained=1', 'p1=[^;]*tank60')
    Shot '03b-client-lamp-dark' 'client'
    Step client relight-lamp | Out-Null
    Check 'lamp relit with the right button' @('drained=0', 'p1=[^;]*tank0')
    Step client walk '-24,12' | Out-Null   # office B, next to its desks
    Step client hide | Out-Null
    Check 'tucked under a desk' @('p1=hp\d+,rv\d+,tucked')
    Shot '05a-client-hiding' 'client'
    if (-not $script:failed) { Invoke-Eval 'return CoOpHorror.Testing.ElecMoleE2E.StandUp();' $Client | Out-Null; Start-Sleep -Seconds 1 }
    Step client drain-cctv 1 | Out-Null
    Check 'camera drained' @('cam1=off', 'p1=[^;]*tank40')
    Step host tx-cheat 2450 | Out-Null
    Step client suck 'Microwave' 240 | Out-Null
    Step client inject 180 | Out-Null
    if (-not $script:failed) {
        Start-Sleep -Milliseconds 1200
        Shot '05-client-transmitter-full' 'client'
    }
    # Full: no blackout yet - the launch button is armed and the surplus became money.
    Check 'transmitter full, surplus exchanged' @('money=9\d', 'stage=Exploring', 'tx=Charging', 'stored=2500/2500', 'microwave=0/1000W')

    # Radio: walkie outside is heard in the ship.
    Step client radio on | Out-Null
    Check 'walkie transmitting' @('radio=Walkie') 'client'
    Step client radio off | Out-Null

    # Death, ghost, revive by hitting, carry the body aboard, launch revive.
    Step client outside | Out-Null
    Step host kill 1 | Out-Null
    Check 'client died' @('p1=hp0,dead', 'corpses=1', 'corpse1=out')
    Step client spectate-check | Out-Null
    Shot '07-client-ghost' 'client'
    Step host revive-hit 1 | Out-Null
    Check 'revived by hits' @('p1=hp1,rv0', 'corpses=0')
    Step host kill 1 | Out-Null
    Step host carry-corpse 1 | Out-Null
    Check 'body aboard' @('corpse1=ship')
    Step host launch | Out-Null
    Check 'launch fired the transmitter and blacked out' @('stage=Launching', 'tx=Transmitting', 'lamps=0')
    Shot '06-host-launch-blackout'
    Step host wait-escape | Out-Null
    Check 'escaped with revived client' @('stage=Escaped', 'earned=2500', 'tx=Done', 'aboard=(0,1|1,0);', 'p1=hp50', 'corpses=0')
    Shot '08-host-escaped'
    Shot '09-client-escaped' 'client'
}
catch {
    Write-Step 'runner' 'fail' $_.Exception.Message $totalWatch.Elapsed.TotalSeconds
}
finally {
    # Collect the in-game step logs from both processes.
    $logs = ''
    try { $logs += "## Host`n" + (Invoke-Eval 'return CoOpHorror.Testing.ElecMoleE2E.TakeLog();') + "`n" } catch { }
    try { $logs += "## Client`n" + (Invoke-Eval 'return CoOpHorror.Testing.ElecMoleE2E.TakeLog();' $Client) + "`n" } catch { }
    if (-not $KeepPlaying) {
        try { Invoke-Cli @('editor.playmode', 'stop_scenario') | Out-Null } catch { }
        # Back to normal Play (main Editor only); the debug panel launches Player 2 on demand.
        Start-Sleep -Seconds 5
        try { Invoke-Eval 'CoOpHorror.Editor.DevToolsEditorBridge.UseMainEditorOnly(); return "main-only";' | Out-Null } catch { }
    }
    $passed = @($results | Where-Object { $_.Status -eq 'ok' }).Count
    $report = New-Object System.Text.StringBuilder
    [void]$report.AppendLine("# ElecMole E2E $stamp")
    [void]$report.AppendLine('')
    [void]$report.AppendLine(("Result: **{0}** - {1}/{2} passed, {3:0} s" -f $(if ($script:failed) { 'FAILED' } else { 'PASSED' }), $passed, $results.Count, $totalWatch.Elapsed.TotalSeconds))
    [void]$report.AppendLine('')
    [void]$report.AppendLine('| Step | Result | s | Detail |')
    [void]$report.AppendLine('|---|---|---|---|')
    foreach ($r in $results) { [void]$report.AppendLine(("| {0} | {1} | {2} | {3} |" -f $r.Step, $r.Status, $r.Seconds, ($r.Detail -replace '\|', '/'))) }
    [void]$report.AppendLine('')
    [void]$report.AppendLine('Screenshots are next to this file.')
    [void]$report.AppendLine('')
    [void]$report.AppendLine('```')
    [void]$report.AppendLine($logs)
    [void]$report.AppendLine('```')
    [IO.File]::WriteAllText((Join-Path $out 'report.md'), $report.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Report: $(Join-Path $out 'report.md')"
}
if ($script:failed) { exit 1 } else { exit 0 }
