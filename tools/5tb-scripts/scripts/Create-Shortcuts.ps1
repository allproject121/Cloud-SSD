# Script to create desktop shortcuts
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "Common.ps1")

$desktop = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$wsh = New-Object -ComObject WScript.Shell

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "                CREATING DESKTOP SHORTCUTS                  " -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Manager Shortcut
$managerTarget = Join-Path $BaseDir "CloudSSD-Manager.bat"
$managerShortcut = $wsh.CreateShortcut((Join-Path $desktop "Cloud-SSD Manager.lnk"))
$managerShortcut.TargetPath = $managerTarget
$managerShortcut.WorkingDirectory = $BaseDir
$managerShortcut.Description = "Manage Cloud-SSD (5TB) Accounts and Storage"
$managerShortcut.IconLocation = "shell32.dll,27" # Hard drive icon
$managerShortcut.Save()

# 2. Mount Shortcut
$mountTarget = Join-Path $BaseDir "Mount-Disk.bat"
$mountShortcut = $wsh.CreateShortcut((Join-Path $desktop "Mount Cloud-SSD (Z).lnk"))
$mountShortcut.TargetPath = $mountTarget
$mountShortcut.WorkingDirectory = $BaseDir
$mountShortcut.Description = "Mount Cloud-SSD 5TB as Local Disk Z:"
$mountShortcut.IconLocation = "shell32.dll,9" # Connect / Drive icon
$mountShortcut.Save()

# 3. Unmount Shortcut
$unmountTarget = Join-Path $BaseDir "Unmount-Disk.bat"
$unmountShortcut = $wsh.CreateShortcut((Join-Path $desktop "Unmount Cloud-SSD (Z).lnk"))
$unmountShortcut.TargetPath = $unmountTarget
$unmountShortcut.WorkingDirectory = $BaseDir
$unmountShortcut.Description = "Unmount Cloud-SSD 5TB"
$unmountShortcut.IconLocation = "shell32.dll,219" # Eject icon
$unmountShortcut.Save()

Write-Host "[SUCCESS] 3 shortcuts created on your Desktop:" -ForegroundColor Green
Write-Host " 1. 'Cloud-SSD Manager' (Settings & Accounts)" -ForegroundColor White
Write-Host " 2. 'Mount Cloud-SSD (Z)' (1-Click Start)" -ForegroundColor White
Write-Host " 3. 'Unmount Cloud-SSD (Z)' (1-Click Stop)" -ForegroundColor White

Write-Host "`nPress any key to return..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
