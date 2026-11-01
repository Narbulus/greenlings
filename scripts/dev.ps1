<#
.SYNOPSIS
  Dev loop for Greenlings.

.EXAMPLE
  ./scripts/dev.ps1 run                 # build, restart game on dev profile, wait until in-world
  ./scripts/dev.ps1 cmd "sg_info" "!screenshot test"   # run console commands / directives in the live game
  ./scripts/dev.ps1 build               # build + deploy only
  ./scripts/dev.ps1 stop                # close the dev game
  ./scripts/dev.ps1 log [-Lines 80]     # tail the BepInEx log

Directives understood by DevTools (prefix "!"): !wait <sec>, !screenshot <name>, !pos, !quit.
Anything else is a Valheim console command (cheats are on in the dev world).
#>
param(
    [Parameter(Position = 0)][ValidateSet('run', 'build', 'launch', 'cmd', 'stop', 'log')][string]$Action = 'run',
    [Parameter(Position = 1, ValueFromRemainingArguments)][string[]]$Commands,
    [int]$Lines = 60,
    [int]$TimeoutSec = 180
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
$DevProfile = Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim\profiles\GreenlingsDev'
$Log = Join-Path $DevProfile 'BepInEx\LogOutput.log'
$DevDir = Join-Path $DevProfile 'BepInEx\devtools'

function Get-DevGame {
    Get-CimInstance Win32_Process -Filter "Name='valheim.exe'" |
        Where-Object { $_.CommandLine -like '*GreenlingsDev*' }
}

function Assert-NoOtherGame {
    $other = Get-CimInstance Win32_Process -Filter "Name='valheim.exe'" |
        Where-Object { $_.CommandLine -notlike '*GreenlingsDev*' }
    if ($other) { throw "Valheim is already running outside the dev profile (pid $($other.ProcessId)). Close it first." }
}

function Stop-DevGame {
    $p = Get-DevGame
    if (-not $p) { return }
    Write-Host "stopping dev game (pid $($p.ProcessId))"
    Stop-Process -Id $p.ProcessId -Force
    while (Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 200 }
}

function Invoke-Build {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # The game locks plugin DLLs, so deploy only works with the dev game closed.
    dotnet build "$Root\Greenlings.sln" -v q -nologo -clp:NoSummary
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
    Write-Host ("build+deploy {0:n1}s" -f $sw.Elapsed.TotalSeconds)
}

# Wait for a log line matching $Pattern that appears after byte offset $From.
function Wait-Log([string]$Pattern, [long]$From, [int]$Timeout = $TimeoutSec, [switch]$Echo) {
    $deadline = (Get-Date).AddSeconds($Timeout)
    $pos = $From
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $Log) {
            $fs = [IO.File]::Open($Log, 'Open', 'Read', 'ReadWrite')
            try {
                if ($fs.Length -lt $pos) { $pos = 0 }  # log was recreated
                $fs.Seek($pos, 'Begin') | Out-Null
                $text = (New-Object IO.StreamReader($fs)).ReadToEnd()
                $pos = $fs.Length
            } finally { $fs.Dispose() }
            foreach ($l in ($text -split "`r?`n")) {
                if ($Echo -and $l -match '\[GLDEV\]|\[console\]|Error|Exception|Greenlings') { Write-Host "  $l" }
                if ($l -match $Pattern) { return $l }
            }
        }
        if ($Action -ne 'cmd' -and -not (Get-DevGame)) { throw 'dev game exited' }
        Start-Sleep -Milliseconds 250
    }
    throw "timed out waiting for /$Pattern/"
}

function Start-DevGame {
    Assert-NoOtherGame
    if (Get-DevGame) { Write-Host 'dev game already running'; return }
    if (-not (Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Steam must be running.' }
    $preloader = Join-Path $DevProfile 'BepInEx\core\BepInEx.Preloader.dll'
    $gameArgs = @('--doorstop-enabled', 'true', '--doorstop-target-assembly', "`"$preloader`"", '-console')
    $sw = [Diagnostics.Stopwatch]::StartNew()
    if (Test-Path $Log) { Remove-Item $Log -ErrorAction SilentlyContinue }
    Start-Process -FilePath "$GameDir\valheim.exe" -ArgumentList $gameArgs -WorkingDirectory $GameDir | Out-Null
    Wait-Log '\[GLDEV\] LOADED' 0 | Out-Null
    Write-Host ("plugins loaded   {0,5:n1}s" -f $sw.Elapsed.TotalSeconds)
    Wait-Log '\[GLDEV\] MENU' 0 | Out-Null
    Write-Host ("main menu        {0,5:n1}s" -f $sw.Elapsed.TotalSeconds)
    $l = Wait-Log '\[GLDEV\] INGAME|AUTOSTART FAILED' 0
    if ($l -match 'FAILED') { throw $l }
    Write-Host ("in world         {0,5:n1}s   $($l -replace '.*INGAME ', 'player at ')" -f $sw.Elapsed.TotalSeconds)
}

function Send-Commands([string[]]$cmds) {
    if (-not (Get-DevGame)) { throw 'dev game is not running (./scripts/dev.ps1 run)' }
    $from = (Get-Item $Log).Length
    New-Item -ItemType Directory -Force $DevDir | Out-Null
    $tmp = Join-Path $DevDir 'cmd.tmp'
    Set-Content -Path $tmp -Value $cmds -Encoding utf8
    Move-Item -Force $tmp (Join-Path $DevDir 'cmd.txt')  # atomic hand-off
    Wait-Log '\[GLDEV\] CMD DONE' $from -Echo | Out-Null
}

switch ($Action) {
    'build'  { Invoke-Build }
    'launch' { Start-DevGame }
    'run'    { Assert-NoOtherGame; Stop-DevGame; Invoke-Build; Start-DevGame }
    'cmd'    { Send-Commands $Commands }
    'stop'   { Stop-DevGame }
    'log'    { Get-Content $Log -Tail $Lines }
}
