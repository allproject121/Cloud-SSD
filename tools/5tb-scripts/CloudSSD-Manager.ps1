# Cloud-SSD (5TB) Main Control Center
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ScriptsFolder = Join-Path $ScriptDir "scripts"
. (Join-Path $ScriptsFolder "Common.ps1")

function Show-Header {
    Clear-Host
    $isMounted = Is-DriveMounted $DefaultDriveLetter
    $accounts = Get-ConnectedAccounts

    Write-Host "======================================================================" -ForegroundColor Cyan
    Write-Host "               CLOUD-SSD (5TB) - VIRTUAL LOCAL DISK                   " -ForegroundColor Yellow
    Write-Host "             Combine Multiple Accounts Into One Storage               " -ForegroundColor DarkGray
    Write-Host "======================================================================" -ForegroundColor Cyan
    
    if ($isMounted) {
        Write-Host " Status:     [ MOUNTED & ACTIVE on $DefaultDriveLetter ]" -ForegroundColor Green
    } else {
        Write-Host " Status:     [ NOT MOUNTED (Disk Offline) ]" -ForegroundColor Red
    }

    Write-Host " Accounts:   $($accounts.Count) Google / Cloud Accounts Connected" -ForegroundColor White
    if ($accounts.Count -gt 0) {
        $names = ($accounts | ForEach-Object { $_.Name }) -join ", "
        Write-Host "             ($names)" -ForegroundColor DarkGray
    } else {
        Write-Host "             (No accounts connected. Use option [1] to begin)" -ForegroundColor Yellow
    }

    $startupFolder = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
    $vbsPath = Join-Path $startupFolder "Start-CloudSSD.vbs"
    $autoStart = Test-Path $vbsPath
    if ($autoStart) {
        Write-Host " Auto-Start: [ ENABLED (Mounts silently on Windows boot) ]" -ForegroundColor Green
    } else {
        Write-Host " Auto-Start: [ DISABLED ]" -ForegroundColor DarkGray
    }

    Write-Host " Local Cache: Max 15 GB on SSD (67 GB+ protected)" -ForegroundColor DarkCyan
    Write-Host "======================================================================" -ForegroundColor Cyan
}

while ($true) {
    Show-Header
    Write-Host ""
    Write-Host " [1]  Add Google Account (One-click Browser Sign-in)" -ForegroundColor White
    Write-Host " [2]  Remove an Account" -ForegroundColor White
    Write-Host " [3]  Check Storage & Accounts Status" -ForegroundColor White
    Write-Host "----------------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host " [4]  Mount 5TB Local Disk (Start $DefaultDriveLetter in Explorer)" -ForegroundColor Green
    Write-Host " [5]  Unmount Disk (Stop $DefaultDriveLetter)" -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host " [6]  Enable Auto-Start on Windows Boot (Silent)" -ForegroundColor Cyan
    Write-Host " [7]  Disable Auto-Start on Windows Boot" -ForegroundColor Cyan
    Write-Host " [8]  Create Desktop Shortcuts" -ForegroundColor Cyan
    Write-Host " [9]  Open Cloud-SSD Settings Folder in Explorer" -ForegroundColor Gray
    Write-Host " [0]  Exit" -ForegroundColor Gray
    Write-Host "======================================================================" -ForegroundColor Cyan

    $choice = Read-Host " Enter choice [0-9]"

    switch ($choice) {
        "1" {
            & (Join-Path $ScriptsFolder "Add-Account.ps1")
        }
        "2" {
            & (Join-Path $ScriptsFolder "Remove-Account.ps1")
        }
        "3" {
            & (Join-Path $ScriptsFolder "List-Accounts.ps1")
        }
        "4" {
            & (Join-Path $ScriptsFolder "Mount-Disk.ps1")
        }
        "5" {
            & (Join-Path $ScriptsFolder "Unmount-Disk.ps1")
        }
        "6" {
            & (Join-Path $ScriptsFolder "Setup-AutoStart.ps1")
        }
        "7" {
            & (Join-Path $ScriptsFolder "Setup-AutoStart.ps1") -Disable
        }
        "8" {
            & (Join-Path $ScriptsFolder "Create-Shortcuts.ps1")
        }
        "9" {
            Start-Process explorer.exe $ScriptDir
        }
        "0" {
            Write-Host "`nGoodbye!" -ForegroundColor Green
            exit 0
        }
        default {
            Write-Host "Invalid option. Please choose between 0 and 9." -ForegroundColor Red
            Start-Sleep -Seconds 1
        }
    }
}
