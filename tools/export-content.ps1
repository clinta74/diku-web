<#
.SYNOPSIS
    Writes the authored world out as a re-runnable SQL script.

.DESCRIPTION
    PLAN.md §6 makes Postgres the only source of truth for content, so a database that gets
    dropped takes the world with it - and the seeder only rebuilds twelve rooms of Millbrook.
    Run this before anything that resets the database: a migration squash, a schema experiment,
    a fresh start.

    Every statement is an upsert, so the output can be applied to a database that has already
    been seeded or to a bare migrated one without knowing which.

    See tools/export-content.sql for what is exported and what deliberately is not.

.EXAMPLE
    tools/export-content.ps1
    tools/export-content.ps1 -Database dikuweb_staging -OutFile backups/staging.sql
#>
[CmdletBinding()]
param(
    [string] $Container = 'dikuweb-postgres',
    [string] $Database = 'dikuweb',
    [string] $User = 'dikuweb',
    [string] $OutFile
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$script = Join-Path $PSScriptRoot 'export-content.sql'

if (-not $OutFile) {
    $stamp = Get-Date -Format 'yyyy-MM-dd'
    $OutFile = Join-Path $repoRoot "backups/content-$stamp.sql"
}

$backupDir = Split-Path -Parent $OutFile
if (-not (Test-Path $backupDir)) {
    New-Item -ItemType Directory -Force $backupDir | Out-Null
}

# Copied in rather than piped on stdin: psql reads a script from a path, and a path inside the
# container is the one thing both sides agree on.
docker cp $script "${Container}:/tmp/export-content.sql"
if ($LASTEXITCODE -ne 0) { throw "Could not copy the export script into $Container." }

docker exec $Container psql -U $User -d $Database -q -f /tmp/export-content.sql |
    Set-Content -Path $OutFile -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw "psql failed against $Database." }

$statements = (Select-String -Path $OutFile -Pattern '^INSERT INTO' -AllMatches).Count
Write-Host "Wrote $statements upserts to $OutFile"

if ($statements -eq 0) {
    Write-Warning 'No content found. Check the database name.'
}
