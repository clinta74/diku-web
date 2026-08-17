<#
.SYNOPSIS
    Writes one account and its characters out as a re-runnable SQL script, for moving them to
    another server.

.DESCRIPTION
    The complement of export-content.ps1, which carries the world and deliberately leaves player
    data behind. This carries the player data and deliberately leaves the world behind: run the
    world in first, with `POST /api/builder/import` or the content export, and then this.

    It writes a file. **It does not touch the target** - applying the result to production is a
    separate command you run yourself, printed at the end. See tools/export-players.sql for what
    travels, what refuses, and why re-running is destructive by design.

.PARAMETER Account
    Username or email of the account to move. Required: there is no default, because a default
    here would be "everybody".

.PARAMETER Relocate
    Rewrite every character's room to this key on the way out. Use it when the target does not
    have the world a character was last standing in - otherwise they are silently relocated to the
    configured starting room on their next login, which works but tells nobody.

.EXAMPLE
    tools/export-players.ps1 -Account clint

.EXAMPLE
    # Atrox is standing in aldenmoor, which production does not have.
    tools/export-players.ps1 -Account clint -Relocate ossara.gatetown.the-gate-yard
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Account,

    [string] $Container = 'dikuweb-postgres',
    [string] $Database = 'dikuweb',
    [string] $User = 'dikuweb',
    [string] $Relocate,
    [string] $OutFile
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$script = Join-Path $PSScriptRoot 'export-players.sql'

if (-not $OutFile) {
    $stamp = Get-Date -Format 'yyyy-MM-dd'
    $safe = $Account -replace '[^A-Za-z0-9._-]', '_'
    $OutFile = Join-Path $repoRoot "backups/players-$safe-$stamp.sql"
}

$backupDir = Split-Path -Parent $OutFile
if (-not (Test-Path $backupDir)) {
    New-Item -ItemType Directory -Force $backupDir | Out-Null
}

# Copied in rather than piped on stdin, for the reason the content export gives: psql reads a
# script from a path, and a path inside the container is the one thing both sides agree on.
docker cp $script "${Container}:/tmp/export-players.sql"
if ($LASTEXITCODE -ne 0) { throw "Could not copy the export script into $Container." }

docker exec $Container psql -U $User -d $Database -q -v account=$Account -f /tmp/export-players.sql |
    Set-Content -Path $OutFile -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw "psql failed against $Database." }

# The generated file is checked rather than the exit code, because the failure this is guarding
# against does not set one. psql exits 0 having written a file with no rows in it when the account
# does not match, and it exits 0 having written "Output format is unaligned." as line 1 if the -q
# above is ever dropped - which is not SQL, and fails at the far end rather than here.
$content = Get-Content -Path $OutFile -Raw

if ($content -notmatch '(?m)^BEGIN;' -or $content -notmatch '(?m)^COMMIT;') {
    throw "No account matched '$Account', or psql wrote something unexpected. See $OutFile."
}

if ($content -notmatch '(?m)^INSERT INTO characters ') {
    throw "'$Account' matched but has no characters. Nothing to move."
}

if ($Relocate) {
    # Applied to the generated SQL rather than to the query, so the export stays a faithful copy of
    # the database and the rewrite is visible in the diff of the file somebody is about to apply.
    if ($Relocate -notmatch '^[a-z0-9-]+\.[a-z0-9-]+\.[a-z0-9-]+$') {
        throw "-Relocate wants a room key of exactly three dot-separated segments, got '$Relocate'."
    }

    $rewritten = [regex]::Replace(
        $content,
        "(?m)(^INSERT INTO characters .*?VALUES \((?:[^,]*,){6}\s*)'[^']*'",
        "`${1}'$Relocate'")

    Set-Content -Path $OutFile -Value $rewritten -Encoding utf8
    $content = $rewritten
    Write-Host "Rewrote every character's room to $Relocate"
}

$characters = ([regex]::Matches($content, '(?m)^INSERT INTO characters ')).Count
$items = ([regex]::Matches($content, '(?m)^INSERT INTO item_instances ')).Count
$quests = ([regex]::Matches($content, '(?m)^INSERT INTO character_quests ')).Count

Write-Host ""
Write-Host "Wrote $OutFile"
Write-Host "  $characters characters, $items items, $quests quest rows"
Write-Host ""

# Rooms and templates the target has to already have. Reported rather than checked, because the
# target is not something this script has a connection to - and it is the one class of problem
# that produces no error at all, just a character somewhere they did not expect to be.
$rooms = [regex]::Matches($content, "(?m)^INSERT INTO characters .*?VALUES \((?:[^,]*,){6}\s*'([^']*)'") |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$templates = [regex]::Matches($content, "(?m)^INSERT INTO item_instances \([^)]*\) VALUES \('[^']*',\s*'([^']*)'") |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

Write-Host "The target needs these rooms to exist, or the characters in them are relocated on login:"
$rooms | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "and these item templates, or their items lose their rules (slot, light, restrictions):"
$templates | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "Nothing has been sent anywhere. To apply it, against the target:"
Write-Host "  psql -v ON_ERROR_STOP=1 -f `"$OutFile`""
Write-Host ""
Write-Host "ON_ERROR_STOP=1 is what makes the refusals in the file mean anything - without it psql"
Write-Host "reports the error, carries on past it, and commits the rest."
