<#
.SYNOPSIS
    Rehearses a full recovery: restore a dump, then start the server against it.

.DESCRIPTION
    PLAN.md §6 Phase 6 asks for "scheduled pg_dump backups + a rehearsed restore drill", and the
    rehearsal is the point. tools/backup/backup.sh already proves every dump restores - it takes
    one and immediately loads it into a scratch database, and deletes any file that will not go
    back in. This script answers the question that one cannot: **does the application start
    against what came out of the backup?**

    That is a different question and it has a different failure mode. A dump restores perfectly and
    the server still refuses to boot if the dump predates a migration that has since been squashed,
    if a migration is half-recorded in __EFMigrationsHistory, or if the restore ran as a role the
    app cannot connect as. None of those touch a single row, so a row-count comparison passes and
    the recovery still fails at the moment you need it.

    What it does, against a scratch database that is dropped afterwards:

      1. Restores the dump (newest in backups/ by default).
      2. Starts Muwbta.Server pointed at it on a spare port, which is what runs the startup
         migrations - §6.1 makes startup the only place migrations are applied, so this is the
         real code path and not an approximation of it.
      3. Waits for /health/ready, which includes the database check.
      4. Asks the API for something the world had to load to answer, so "it started" means the
         world came back rather than that the process is alive.
      5. Stops the server and drops the database, on the way out of every branch.

    Nothing here touches the live database. The drill is safe to run whenever, and the whole point
    is that it is run *before* an incident rather than during one.

.EXAMPLE
    tools/restore-drill.ps1
    tools/restore-drill.ps1 -Dump backups/dikuweb-2026-08-14T143917Z.dump
#>
[CmdletBinding()]
param(
    # Defaults to the newest dump in backups/.
    [string] $Dump,
    [string] $Container = 'dikuweb-postgres',
    [string] $User = 'dikuweb',
    # Read from .env, because that is what docker-compose.yml handed the container. Defaulting to
    # the compose fallback instead would produce a drill that fails on authentication and reads as
    # "the backup is bad", which is the most misleading way for a recovery rehearsal to fail.
    [string] $Password,
    [string] $DrillDatabase = 'dikuweb_drill',
    # Not 5180: the drill must not collide with a dev server somebody has running.
    [int] $Port = 5199
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$server = $null
$restored = $false

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Ok($text) { Write-Host "   $text" -ForegroundColor Green }

try {
    if (-not $Password) {
        # Read off the running container rather than out of .env. The two are not the same thing:
        # POSTGRES_PASSWORD only applies at initdb, so a cluster keeps whatever it was created
        # with no matter what the compose file says today - and in this repo they have already
        # drifted, with .env setting POSTGRES_* while docker-compose.yml interpolates DB_*. The
        # container knows what it was actually built with; the files only know what was intended.
        $Password = (docker inspect $Container --format '{{range .Config.Env}}{{println .}}{{end}}' |
            Select-String '^POSTGRES_PASSWORD=(.*)$').Matches.Groups[1].Value
        if (-not $Password) { throw "Could not read POSTGRES_PASSWORD from $Container. Pass -Password." }
    }

    if (-not $Dump) {
        # Matched on the timestamp shape the sidecar writes, not on '*.dump'. backups/ also holds
        # dumps taken by hand with arbitrary names, and a plain wildcard sorted by name picks
        # 'dikuweb-full-2026-08-10.dump' over 'dikuweb-2026-08-14T…' because 'f' sorts after a
        # digit - so the default drilled the oldest file in the directory while claiming to drill
        # the newest. Found by running it.
        $newest = Get-ChildItem (Join-Path $repoRoot 'backups') -ErrorAction SilentlyContinue |
            Where-Object Name -match '^dikuweb-\d{4}-\d{2}-\d{2}T\d{6}Z\.dump$' |
            Sort-Object Name -Descending | Select-Object -First 1
        if (-not $newest) {
            throw 'No scheduled dump found in backups/. Take one: docker compose -f docker-compose.prod.yml exec backup /scripts/backup.sh --once'
        }
        $Dump = $newest.FullName
    }

    if (-not (Test-Path $Dump)) { throw "No such dump: $Dump" }
    $Dump = (Resolve-Path $Dump).Path
    Step "Drilling $([IO.Path]::GetFileName($Dump)) ($([math]::Round((Get-Item $Dump).Length / 1KB, 1)) KB)"

    # ---------------------------------------------------------------- restore
    Step "Restoring into $DrillDatabase"
    docker cp $Dump "${Container}:/tmp/drill.dump" | Out-Null
    docker exec $Container psql -U $User -d postgres -q -c "drop database if exists $DrillDatabase;" | Out-Null
    docker exec $Container psql -U $User -d postgres -q -c "create database $DrillDatabase;" | Out-Null
    $restored = $true

    # --no-owner --no-privileges for the reason backup.sh gives: a recovery onto a rebuilt host is
    # not performed by the role that took the dump, and a restore that only works as the original
    # owner is one that fails on the day it matters.
    docker exec $Container pg_restore -U $User -d $DrillDatabase --no-owner --no-privileges /tmp/drill.dump
    if ($LASTEXITCODE -ne 0) { throw 'pg_restore reported errors. The dump is not usable.' }
    Ok 'restored with no errors'

    # format('%I') rather than a quoted identifier in the client string: __EFMigrationsHistory is
    # mixed case and needs double quotes, and those do not survive the trip through docker exec -
    # Postgres then folds the name to lower case and reports the table as missing. Quoting the
    # identifier server-side keeps the whole statement free of double quotes.
    $applied = docker exec $Container psql -U $User -d $DrillDatabase -At -c `
        "select (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I', '__EFMigrationsHistory'), false, true, '')))[1]::text;"
    if ($LASTEXITCODE -ne 0) {
        throw 'The restored database has no __EFMigrationsHistory. Startup will try to build the schema from scratch on top of the restored tables and fail.'
    }
    Ok "$applied migrations recorded in the restored history"

    # ---------------------------------------------------------------- boot
    Step 'Starting the server against it'
    $env:ConnectionStrings__Muwbta = "Host=localhost;Port=5432;Database=$DrillDatabase;Username=$User;Password=$Password"
    $env:ASPNETCORE_URLS = "http://localhost:$Port"
    $env:ASPNETCORE_ENVIRONMENT = 'Development'

    $log = Join-Path ([IO.Path]::GetTempPath()) "drill-$PID.log"
    $server = Start-Process -PassThru -NoNewWindow -FilePath 'dotnet' `
        -ArgumentList 'run', '--project', (Join-Path $repoRoot 'src/Muwbta.Server'), '--no-launch-profile' `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err"

    # Sixty seconds: a cold `dotnet run` builds first, and a drill that times out during a restore
    # rehearsal teaches the wrong lesson.
    $deadline = (Get-Date).AddSeconds(60)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        if ($server.HasExited) { break }
        try {
            $r = Invoke-WebRequest "http://localhost:$Port/health/ready" -TimeoutSec 3 -ErrorAction Stop
            if ($r.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 700 }
    }

    if (-not $ready) {
        Write-Host (Get-Content $log -Tail 40 -ErrorAction SilentlyContinue) -ForegroundColor DarkGray
        Write-Host (Get-Content "$log.err" -Tail 40 -ErrorAction SilentlyContinue) -ForegroundColor DarkGray
        throw 'The server never became ready against the restored database. The log above is the recovery failing.'
    }
    Ok '/health/ready answered 200 (database check included)'

    # ---------------------------------------------------------------- the world came back
    Step 'Checking the world loaded, not just the process'
    # Read out of the loop's own startup line rather than counted with psql. "The rows are in the
    # database" and "the loop built a world out of them" are different claims, and only the second
    # one is what a restore is for - /health/ready would answer 200 for a schema with no content
    # in it. EngineLog: "Game loop starting with {RoomCount} rooms".
    $loaded = (Get-Content $log -ErrorAction SilentlyContinue |
        Select-String 'Game loop starting with (\d+) rooms').Matches.Groups[1].Value

    if (-not $loaded) {
        throw 'The server started but the game loop never reported loading a world.'
    }
    if ([int]$loaded -eq 0) {
        throw 'The loop started with zero rooms. That is an empty world wearing a working schema.'
    }
    Ok "the game loop came up with $loaded rooms"

    Write-Host "`nDrill passed. $([IO.Path]::GetFileName($Dump)) is a backup you can actually recover from." -ForegroundColor Green
}
finally {
    if ($server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        # dotnet run spawns the app as a child; killing the launcher leaves it holding the port.
        Get-CimInstance Win32_Process -Filter "ParentProcessId = $($server.Id)" -ErrorAction SilentlyContinue |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    }
    if ($restored) {
        docker exec $Container psql -U $User -d postgres -q -c "drop database if exists $DrillDatabase;" | Out-Null
    }
    Remove-Item Env:ConnectionStrings__Muwbta, Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
}
