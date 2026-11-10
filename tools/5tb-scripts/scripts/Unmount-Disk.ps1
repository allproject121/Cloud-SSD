# Script to unmount Cloud-SSD
param(
    [string]$DriveLetter = "Z:",
    [switch]$Silent
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "Common.ps1")

if (-not $Silent) {
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "                UNMOUNTING CLOUD-SSD ($DriveLetter)         " -ForegroundColor Yellow
    Write-Host "============================================================" -ForegroundColor Cyan
}

$unmounted = $false

# 1. Try RC graceful shutdown first
try {
    $res = & "$RcloneExe" rc core/quit --rc-addr "127.0.0.1:$RcPort" 2>$null
    Start-Sleep -Seconds 2
    if (-not (Is-DriveMounted $DriveLetter)) {
        $unmounted = $true
    }
} catch {}

# 2. If PID file exists, stop process
if (Test-Path $PidFile) {
    try {
        $pidNum = Get-Content $PidFile -ErrorAction SilentlyContinue
        if ($pidNum -and (Get-Process -Id $pidNum -ErrorAction SilentlyContinue)) {
            Stop-Process -Id $pidNum -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
        }
    } catch {}
    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

# 3. Fallback kill of any rclone process running from our bin folder
$ourRclone = Get-Process -Name "rclone" -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -eq $RcloneExe } catch { $false }
}
if ($ourRclone) {
    $ourRclone | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

if (-not (Is-DriveMounted $DriveLetter)) {
    if (-not $Silent) {
        Write-Host "[SUCCESS] Cloud-SSD unmounted successfully. Drive $DriveLetter released." -ForegroundColor Green
        Write-Host "`nPress any key to return..." -ForegroundColor Gray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    exit 0
} else {
    if (-not $Silent) {
        Write-Host "[WARNING] Drive $DriveLetter might still be busy or open in Explorer." -ForegroundColor Yellow
        Write-Host "`nPress any key to return..." -ForegroundColor Gray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    exit 1
}
