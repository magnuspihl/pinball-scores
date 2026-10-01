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

    This script ships inside the package, so after installing it sits next to the
    executable and needs no arguments.

.EXAMPLE
    C:\PinballScores\current\Install-PinballScores.ps1 -ApiBaseUrl 'http://scores.example.lan/api'

    There is no default API address, so a first install needs -ApiBaseUrl (or a
    hand-edit of C:\ProgramData\PinballScores\appsettings.json, then rerunning
    this script). Without a valid one the service is registered for manual start
    and not started, so it cannot crash-loop at boot either.

.EXAMPLE
    .\Install-PinballScores.ps1 -ExePath 'C:\PinballScores\current\PinballScores.exe'
#>
[CmdletBinding()]
param(
    # Defaults to the executable sitting beside this script.
    [string]$ExePath,

    [string]$ServiceName = 'PinballScores',

    [string]$DisplayName = 'Pinball Scores',

    # The pinball scores API root, including /api, e.g. http://scores.example.lan/api.
    # Written into the machine-level settings, replacing whatever is there.
    [string]$ApiBaseUrl,

    # Optional. Only needed if the server sets PINBALL_API_KEY.
    [string]$ApiKey
)

$ErrorActionPreference = 'Stop'

if (-not $ExePath) {
    $ExePath = Join-Path $PSScriptRoot 'PinballScores.exe'
    if (-not (Test-Path $ExePath)) {
        throw "Could not find PinballScores.exe next to this script. Pass -ExePath explicitly."
    }
}

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this from an elevated PowerShell prompt.'
}

if (-not (Test-Path $ExePath)) { throw "Not found: $ExePath" }
$ExePath = (Resolve-Path $ExePath).Path

# The same rules the service applies at startup (SyncOptions.Validate), plus the
# retired Foundry host, which validates but fails every sync. Returns the problem,
# or $null for an address the service will accept.
function Test-ApiBaseUrl([string]$Url) {
    if ([string]::IsNullOrWhiteSpace($Url)) { return 'ApiBaseUrl is not set.' }
    $uri = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$uri) -or
        ($uri.Scheme -ne 'http' -and $uri.Scheme -ne 'https')) {
        return "ApiBaseUrl is not an absolute http(s) URL ($Url)."
    }
    if ($uri.Host -like 'foundryapps*.scw.cloud') {
        return "ApiBaseUrl still points at the retired Foundry API ($Url)."
    }
    return $null
}

# A bad address on the command line is refused before the existing service or its
# settings are touched, so a typo neither replaces a working setting nor leaves a
# freshly registered service behind.
if ($PSBoundParameters.ContainsKey('ApiBaseUrl') -and ($bad = Test-ApiBaseUrl $ApiBaseUrl)) {
    throw "-ApiBaseUrl rejected: $bad"
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping existing $ServiceName..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Registered for manual start; it is only switched to automatic at the end, once
# the API address is known to be valid. If anything in between fails, a service
# left on automatic would crash-loop at every boot.
Write-Host "Registering $ServiceName -> $ExePath"
New-Service -Name $ServiceName `
    -BinaryPathName "`"$ExePath`"" `
    -DisplayName $DisplayName `
    -Description 'Extracts pinball high scores and syncs them with the pinball scores API.' `
    -StartupType Manual | Out-Null

# Recovery actions cover genuine crashes only. They are deliberately NOT how the
# auto-update restart works: a clean stop reports SERVICE_STOPPED with exit code 0,
# which SCM treats as a normal shutdown and never recovers from. The updater
# schedules its own restart instead. The failure flag below additionally lets a
# non-zero exit code count as a failure, which is useful for real faults.
sc.exe failure    $ServiceName reset= 86400 actions= restart/30000/restart/60000/restart/120000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

New-Item -ItemType Directory -Force -Path 'C:\ProgramData\PinballScores\logs' | Out-Null

# Seed the machine-level settings from the packaged defaults, but NEVER overwrite
# them. The packaged appsettings.json is replaced by every update, so this is the
# only copy that survives one — and losing a cabinet's configuration to an
# auto-update would be a nasty surprise.
$machineSettings = 'C:\ProgramData\PinballScores\appsettings.json'
$packaged = Join-Path (Split-Path $ExePath -Parent) 'appsettings.json'
if (Test-Path $machineSettings) {
    Write-Host "Keeping existing settings: $machineSettings"
}
elseif (Test-Path $packaged) {
    Copy-Item $packaged $machineSettings
    Write-Host "Created $machineSettings from the packaged defaults."
}

# The settings files carry // comments, which Windows PowerShell's ConvertFrom-Json
# rejects, so single string settings are read and replaced in the text instead.
# Editing in place keeps the comments and every other setting untouched.
function Get-JsonString([string]$Path, [string]$Name) {
    if (-not (Test-Path $Path)) { return $null }
    $m = [regex]::Match((Get-Content -Raw $Path), '"' + $Name + '"\s*:\s*"((?:[^"\\]|\\.)*)"')
    if ($m.Success) { return $m.Groups[1].Value.Replace('\"', '"').Replace('\\', '\') } else { return $null }
}

function Set-JsonString([string]$Path, [string]$Name, [string]$Value) {
    if (-not (Test-Path $Path)) { throw "$Path does not exist, so $Name cannot be written to it." }
    $text = Get-Content -Raw $Path
    $pattern = '"' + $Name + '"\s*:\s*(?:"(?:[^"\\]|\\.)*"|null)'
    if (-not [regex]::IsMatch($text, $pattern)) {
        throw "$Path has no `"$Name`" setting to update. Add it under `"PinballScores`" by hand."
    }
    $escaped = $Value.Replace('\', '\\').Replace('"', '\"')
    $text = [regex]::Replace($text, $pattern, { param($m) "`"$Name`": `"$escaped`"" }, 'None')
    Set-Content -Path $Path -Value $text -NoNewline -Encoding UTF8
}

if ($PSBoundParameters.ContainsKey('ApiBaseUrl')) {
    Set-JsonString $machineSettings 'ApiBaseUrl' $ApiBaseUrl
    Write-Host "Set ApiBaseUrl = $ApiBaseUrl"
}
if ($PSBoundParameters.ContainsKey('ApiKey')) {
    Set-JsonString $machineSettings 'ApiKey' $ApiKey
    Write-Host 'Set ApiKey'
}

# The machine-level file overrides the packaged one, so it decides the address.
$effectiveUrl = Get-JsonString $machineSettings 'ApiBaseUrl'
if ($null -eq $effectiveUrl) { $effectiveUrl = Get-JsonString $packaged 'ApiBaseUrl' }

# Starting with no address would fail validation at startup, and the recovery
# actions above would then restart it over and over. The retired Foundry staging
# host is refused for a related reason: a config seeded before the move to self-hosting
# still holds it, and the service would start and quietly fail every sync.
$problem = Test-ApiBaseUrl $effectiveUrl

if ($problem) {
    # Manual start, not just "not started now": left on automatic, Windows would
    # start it at the next boot into the same failure and restart loop. Rerunning
    # this script once the address is fixed switches it back to automatic.
    sc.exe config $ServiceName start= demand | Out-Null
    Write-Warning "$problem The service is registered for MANUAL start and NOT started."
    Write-Warning "Set ApiBaseUrl in $machineSettings (e.g. http://scores.example.lan/api), then rerun this script so it starts automatically again."
}
else {
    # Delay the automatic start so the service is not competing with the cabinet's
    # front end during boot.
    sc.exe config $ServiceName start= delayed-auto | Out-Null
    Write-Host "Starting against $effectiveUrl..."
    Start-Service -Name $ServiceName
}
Get-Service -Name $ServiceName | Format-List Name, Status, StartType

Write-Host ''
Write-Host 'Configuration: C:\ProgramData\PinballScores\appsettings.json  (survives updates - edit this one)'
Write-Host 'Logs:          C:\ProgramData\PinballScores\logs'
Write-Host ''
Write-Host 'Verify without touching anything:'
Write-Host "  & '$ExePath' --plan"
