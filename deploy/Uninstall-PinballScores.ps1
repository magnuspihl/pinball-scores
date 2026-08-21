<#
.SYNOPSIS
    Removes the PinballScores service.

.DESCRIPTION
    Leaves configuration and logs under C:\ProgramData\PinballScores alone unless
    -Purge is given.
#>
[CmdletBinding()]
param(
    [string]$ServiceName = 'PinballScores',
    [switch]$Purge
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this from an elevated PowerShell prompt.'
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Removed $ServiceName."
}
else {
    Write-Host "$ServiceName is not installed."
}

if ($Purge) {
    Remove-Item -Recurse -Force 'C:\ProgramData\PinballScores' -ErrorAction SilentlyContinue
    Write-Host 'Removed configuration and logs.'
}
