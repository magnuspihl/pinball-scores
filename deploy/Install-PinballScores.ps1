<#
.SYNOPSIS
    Registers PinballScores as a Windows service on the cabinet.

.DESCRIPTION
    Run once, from an elevated PowerShell prompt, after the Velopack installer.
    Afterwards the app updates itself, so this should not need running again.

    The service runs as LocalSystem in session 0: no desktop, so it cannot show a
    window or take focus no matter what it does.

    Install the package to a fixed path first, NOT the Velopack default:

        PinballScores-win-Setup.exe --installto C:\PinballScores

    Velopack installs per-user to %LocalAppData% unless told otherwise, which a
    LocalSystem service cannot resolve to the same place. Point -ExePath at the
    'current' folder underneath, which stays valid across updates.

.EXAMPLE
    .\Install-PinballScores.ps1 -ExePath 'C:\PinballScores\current\PinballScores.exe'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [string]$ServiceName = 'PinballScores',

    [string]$DisplayName = 'Pinball Scores'
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this from an elevated PowerShell prompt.'
}

if (-not (Test-Path $ExePath)) { throw "Not found: $ExePath" }
$ExePath = (Resolve-Path $ExePath).Path

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping existing $ServiceName..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Registering $ServiceName -> $ExePath"
New-Service -Name $ServiceName `
    -BinaryPathName "`"$ExePath`"" `
    -DisplayName $DisplayName `
    -Description 'Extracts pinball high scores and syncs them with the Foundry API.' `
    -StartupType Automatic | Out-Null

# Recovery actions cover genuine crashes only. They are deliberately NOT how the
# auto-update restart works: a clean stop reports SERVICE_STOPPED with exit code 0,
# which SCM treats as a normal shutdown and never recovers from. The updater
# schedules its own restart instead. The failure flag below additionally lets a
# non-zero exit code count as a failure, which is useful for real faults.
sc.exe failure    $ServiceName reset= 86400 actions= restart/30000/restart/60000/restart/120000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

# Delay the automatic start so the service is not competing with the cabinet's
# front end during boot.
sc.exe config $ServiceName start= delayed-auto | Out-Null

New-Item -ItemType Directory -Force -Path 'C:\ProgramData\PinballScores\logs' | Out-Null

Write-Host 'Starting...'
Start-Service -Name $ServiceName
Get-Service -Name $ServiceName | Format-List Name, Status, StartType

Write-Host ''
Write-Host 'Configuration: C:\ProgramData\PinballScores\appsettings.json (overrides the one next to the exe)'
Write-Host 'Logs:          C:\ProgramData\PinballScores\logs'
Write-Host ''
Write-Host 'Verify without touching anything:'
Write-Host "  & '$ExePath' --plan"
