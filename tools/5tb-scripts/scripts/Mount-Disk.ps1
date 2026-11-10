# Script to mount CloudSSD as a Native Fixed Local Disk (Z:) with TURBO Speed Optimizations
param(
    [string]$DriveLetter = "Z:",
    [switch]$Silent,
    [switch]$NoOpenExplorer
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "Common.ps1")

if (-not $Silent) {
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "     MOUNTING CLOUD-SSD (5TB) - TURBO HIGH-SPEED MODE       " -ForegroundColor Yellow
    Write-Host "============================================================" -ForegroundColor Cyan
}

# 1. Check WinFsp
$winfsp = Get-Service WinFsp.Launcher -ErrorAction SilentlyContinue
if (-not $winfsp -or $winfsp.Status -ne 'Running') {
    $msg = "[ERROR] WinFsp service is not running. Please install WinFsp or start the WinFsp.Launcher service."
    if (-not $Silent) { Write-Host $msg -ForegroundColor Red; Pause }
    exit 1
}

# 2. Check if already mounted
if (Is-DriveMounted $DriveLetter) {
    if (-not $Silent) {
        Write-Host "[INFO] Drive $DriveLetter is already mounted and active!" -ForegroundColor Green
        if (-not $NoOpenExplorer) { Start-Process explorer.exe $DriveLetter }
        Write-Host "`nPress any key to return..." -ForegroundColor Gray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    exit 0
}

# 3. Check for accounts
$accounts = Get-ConnectedAccounts
if ($accounts.Count -eq 0) {
    $msg = "[ERROR] No accounts connected! Please add at least one Google account using option [1] first."
    if (-not $Silent) { Write-Host $msg -ForegroundColor Red; Pause }
    exit 1
}

# Ensure Union section is up to date
Update-CloudSSDUnion

$argList = "mount CloudSSD: $DriveLetter " +
    "--config `"$ConfigFile`" " +
    "--volname `"$DefaultVolumeName`" " +
    "--file-perms 0777 --dir-perms 0777 --links " +
    "--vfs-cache-mode full " +
    "--vfs-cache-max-size 15G " +
    "--vfs-cache-max-age 24h " +
    "--vfs-cache-min-free-space 10G " +
    "--cache-dir `"$CacheDir`" " +
    "--vfs-read-chunk-size 4M " +
    "--vfs-read-chunk-streams 4 " +
    "--vfs-read-ahead 64M " +
    "--vfs-fast-fingerprint " +
    "--no-checksum " +
    "--drive-chunk-size 64M " +
    "--buffer-size 64M " +
    "--dir-cache-time 72h " +
    "--poll-interval 1m"

if (-not $Silent) {
    Write-Host "[INFO] Launching Cloud-SSD Turbo Mount..." -ForegroundColor Cyan
    Write-Host "   Drive Letter:      $DriveLetter" -ForegroundColor White
    Write-Host "   Read Acceleration: 4 Parallel Streams + 64MB Read-Ahead" -ForegroundColor Green
    Write-Host "   Upload Booster:    64MB Chunk Size (Fast Transfer)" -ForegroundColor Green
    Write-Host "   Cache Limit:       15 GB SSD Reserve`n" -ForegroundColor White
}

# Start rclone in background
$proc = Start-Process -FilePath $RcloneExe -ArgumentList $argList -WindowStyle Hidden -PassThru
Set-Content -Path $PidFile -Value $proc.Id

# Wait for drive letter to appear
$timeoutSeconds = 25
$timer = [System.Diagnostics.Stopwatch]::StartNew()
$mounted = $false

while ($timer.Elapsed.TotalSeconds -lt $timeoutSeconds) {
    if (Is-DriveMounted $DriveLetter) {
        $mounted = $true
        break
    }
    Start-Sleep -Milliseconds 500
}

if ($mounted) {
    if (-not $Silent) {
        Write-Host "[SUCCESS] $DefaultVolumeName is now mounted as Local Disk $DriveLetter!" -ForegroundColor Green
        Write-Host "TURBO speed optimizations are ACTIVE." -ForegroundColor Cyan
        if (-not $NoOpenExplorer) {
            Start-Process explorer.exe $DriveLetter
        }
        Write-Host "`nPress any key to return..." -ForegroundColor Gray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    exit 0
} else {
    $err = "[ERROR] Drive $DriveLetter did not appear within $timeoutSeconds seconds."
    if (-not $Silent) {
        Write-Host $err -ForegroundColor Red
        Pause
    }
    exit 1
}
