param(
    [Parameter(Mandatory)][ValidateSet('patch','minor')][string]$Bump,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Summary,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string[]]$Changes,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string[]]$Validation,
    [string[]]$KnownIssues = @('별도 기록 없음'),
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$versionFile = Join-Path $root 'VERSION'
$changelogFile = Join-Path $root 'CHANGELOG.md'
$readmeFile = Join-Path $root 'README.md'
$old = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ($old -notmatch '^0\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "Pre-release versions must be 0.minor.patch; found '$old'. 1.0.0 is reserved for an explicitly approved public release."
}
$minor = [int]$Matches[1]
$patch = [int]$Matches[2]
if ($Bump -eq 'minor') { $minor++; $patch = 0 } else { $patch++ }
$next = "0.$minor.$patch"
$seoul = [TimeZoneInfo]::FindSystemTimeZoneById('Korea Standard Time')
$date = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $seoul).ToString('yyyy-MM-dd')
$devlogDir = Join-Path $root 'Docs/Devlog'
$devlogFile = Join-Path $devlogDir "$date-v$next.md"
if (Test-Path -LiteralPath $devlogFile) { throw "Devlog already exists: $devlogFile" }
$changelog = Get-Content -LiteralPath $changelogFile -Raw
$readme = Get-Content -LiteralPath $readmeFile -Raw
if (-not $readme.Contains("**현재 게임 버전: $old**")) { throw 'README.md and VERSION must show the same current version.' }
if ($changelog -notmatch '(?m)^# Changelog\r?$' -or $changelog -notmatch ('(?m)^## \[' + [regex]::Escape($old) + '\]')) {
    throw 'VERSION and CHANGELOG.md must agree before creating an update.'
}
if ($Changes.Where({[string]::IsNullOrWhiteSpace($_)}).Count -or $Validation.Where({[string]::IsNullOrWhiteSpace($_)}).Count) {
    throw 'Changes and validation entries must not be blank. Explicitly record untested items.'
}
if ($DryRun) {
    [pscustomobject]@{oldVersion=$old;newVersion=$next;date=$date;devlog=$devlogFile;writesFiles=$false} | ConvertTo-Json
    exit 0
}
$changeLines = ($Changes | ForEach-Object { '- ' + $_ }) -join "`n"
$validationLines = ($Validation | ForEach-Object { '- ' + $_ }) -join "`n"
$issueLines = ($KnownIssues | ForEach-Object { '- ' + $_ }) -join "`n"
$entry = "## [$next] - $date`n`n$Summary`n`n### 변경`n`n$changeLines`n`n### 검증`n`n$validationLines`n`n### 남은 항목`n`n$issueLines`n`n[개발 로그](Docs/Devlog/$date-v$next.md)`n`n"
$header = [regex]::Match($changelog, '\A# Changelog\r?\n')
if (-not $header.Success) { throw 'CHANGELOG.md must begin with # Changelog.' }
$updated = "# Changelog`n`n$entry" + $changelog.Substring($header.Length).TrimStart([char[]]"`r`n")
$devlog = "# v$next 개발 로그 — $date`n`n이전 버전: $old`n`n## 목표`n`n$Summary`n`n## 변경`n`n$changeLines`n`n## 검증과 결과`n`n$validationLines`n`n## 남은 항목`n`n$issueLines`n`n## 다음 작업`n`n- 위 남은 항목을 확인하고 다음 변경 전 관련 문서를 읽는다.`n"
$utf8 = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Force -Path $devlogDir | Out-Null
[IO.File]::WriteAllText($devlogFile, $devlog, $utf8)
[IO.File]::WriteAllText($changelogFile, $updated, $utf8)
[IO.File]::WriteAllText($readmeFile, $readme.Replace("**현재 게임 버전: $old**", "**현재 게임 버전: $next**"), $utf8)
[IO.File]::WriteAllText($versionFile, "$next`n", $utf8)
Write-Host "Version $old -> $next"
Write-Host 'Unity applies VERSION on its next asset refresh; validate the displayed PlayerSettings version before building.'
Write-Host "Review VERSION, README.md, CHANGELOG.md, $devlogFile and ProjectSettings/ProjectSettings.asset together before committing."
