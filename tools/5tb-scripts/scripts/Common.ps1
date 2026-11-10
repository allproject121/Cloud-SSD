# Common configuration and helper functions for Cloud-SSD 5TB
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$BaseDir = Split-Path -Parent $ScriptDir

$RcloneExe = Join-Path $BaseDir "bin\rclone.exe"
$ConfigDir = Join-Path $BaseDir "config"
$ConfigFile = Join-Path $ConfigDir "rclone.conf"
$LogsDir = Join-Path $BaseDir "logs"
$CacheDir = Join-Path $env:LOCALAPPDATA "CloudSSD\cache"
$PidFile = Join-Path $BaseDir "logs\mount.pid"

# Ensure essential directories exist
if (-not (Test-Path $ConfigDir)) { New-Item -ItemType Directory -Path $ConfigDir -Force | Out-Null }
if (-not (Test-Path $LogsDir)) { New-Item -ItemType Directory -Path $LogsDir -Force | Out-Null }
if (-not (Test-Path $CacheDir)) { New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null }
if (-not (Test-Path $ConfigFile)) { New-Item -ItemType File -Path $ConfigFile -Force | Out-Null }

$DefaultDriveLetter = "Z:"
$DefaultVolumeName = "CloudSSD-15TB"
$RcPort = 5572

function Get-ConnectedAccounts {
    $accounts = @()
    if (-not (Test-Path $ConfigFile)) { return $accounts }

    $lines = Get-Content $ConfigFile
    $currentSection = ""
    $currentType = ""

    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^\[([^\]]+)\]$') {
            if ($currentSection -and $currentSection -ne 'CloudSSD' -and $currentType) {
                $accounts += [PSCustomObject]@{
                    Name = $currentSection
                    Type = $currentType
                }
            }
            $currentSection = $matches[1]
            $currentType = ""
        }
        elseif ($trimmed -match '^type\s*=\s*(.+)$') {
            $currentType = $matches[1].Trim()
        }
    }

    if ($currentSection -and $currentSection -ne 'CloudSSD' -and $currentType) {
        $accounts += [PSCustomObject]@{
            Name = $currentSection
            Type = $currentType
        }
    }

    return $accounts
}

function Update-CloudSSDUnion {
    $accounts = Get-ConnectedAccounts
    $driveNames = $accounts | Where-Object { $_.Type -eq 'drive' -or $_.Type -eq 'onedrive' -or $_.Type -eq 'dropbox' } | ForEach-Object { $_.Name }

    # Read current config lines excluding [CloudSSD] block
    $existingLines = @()
    $inCloudSSD = $false

    if (Test-Path $ConfigFile) {
        $rawLines = Get-Content $ConfigFile
        foreach ($line in $rawLines) {
            $trimmed = $line.Trim()
            if ($trimmed -match '^\[([^\]]+)\]$') {
                if ($matches[1] -eq 'CloudSSD') {
                    $inCloudSSD = $true
                    continue
                } else {
                    $inCloudSSD = $false
                }
            }
            if (-not $inCloudSSD) {
                $existingLines += $line
            }
        }
    }

    # Trim trailing blank lines
    while ($existingLines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($existingLines[-1])) {
        $existingLines = $existingLines[0..($existingLines.Count - 2)]
    }

    if ($driveNames.Count -gt 0) {
        $upstreams = ($driveNames | ForEach-Object { "$_`:" }) -join " "
        $unionBlock = @"

[CloudSSD]
type = union
upstreams = $upstreams
action_policy = epmfs
create_policy = mfs
search_policy = ff
"@
        $existingLines += $unionBlock
    }

    Set-Content -Path $ConfigFile -Value ($existingLines -join "`r`n") -Encoding utf8
}

function Is-DriveMounted {
    param([string]$Letter = "Z:")
    $cleanLetter = $Letter.TrimEnd(":\")
    $drives = Get-PSDrive -PSProvider FileSystem | ForEach-Object { $_.Name }
    return ($drives -contains $cleanLetter)
}
