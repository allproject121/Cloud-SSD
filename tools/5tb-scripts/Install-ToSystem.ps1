# Installer for Cloud-SSD (5TB) to Windows System
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\CloudSSD")
)

$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $MyInvocation.MyCommand.Definition

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "       INSTALLING CLOUD-SSD (5TB) TO LAPTOP SYSTEM          " -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Destination: $InstallDir" -ForegroundColor Gray

# 1. Close any running GUI app
Get-Process powershell -ErrorAction SilentlyContinue | Where-Object {
    try { $_.MainWindowTitle -match "Cloud-SSD" } catch { $false }
} | Stop-Process -Force -ErrorAction SilentlyContinue

# 2. Create destination directory
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# 3. Copy program files
$folders = @("bin", "config", "logs", "scripts")
foreach ($f in $folders) {
    $srcPath = Join-Path $src $f
    $destPath = Join-Path $InstallDir $f
    if (Test-Path $srcPath) {
        Copy-Item -Path $srcPath -Destination $destPath -Recurse -Force
    }
}

$files = @("CloudSSD-App.ps1", "Cloud-SSD.vbs", "Uninstall-CloudSSD.ps1", "README.md")
foreach ($f in $files) {
    $srcPath = Join-Path $src $f
    $destPath = Join-Path $InstallDir $f
    if (Test-Path $srcPath) {
        Copy-Item -Path $srcPath -Destination $destPath -Force
    }
}

Write-Host "[SUCCESS] Files copied to $InstallDir" -ForegroundColor Green

# 4. Create Shortcuts
$shell = New-Object -ComObject WScript.Shell
$desktop = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$startMenu = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Programs)
$vbsPath = Join-Path $InstallDir "Cloud-SSD.vbs"

# 4a. Desktop Shortcut
$desktopLnk = $shell.CreateShortcut((Join-Path $desktop "Cloud SSD (5TB).lnk"))
$desktopLnk.TargetPath = "wscript.exe"
$desktopLnk.Arguments = "`"$vbsPath`""
$desktopLnk.WorkingDirectory = $InstallDir
$desktopLnk.Description = "Cloud-SSD (5TB) Control Center"
$desktopLnk.IconLocation = "imageres.dll,27"
$desktopLnk.Save()

# 4b. Start Menu Shortcut
$startMenuLnk = $shell.CreateShortcut((Join-Path $startMenu "Cloud-SSD (5TB).lnk"))
$startMenuLnk.TargetPath = "wscript.exe"
$startMenuLnk.Arguments = "`"$vbsPath`""
$startMenuLnk.WorkingDirectory = $InstallDir
$startMenuLnk.Description = "Cloud-SSD (5TB) Control Center"
$startMenuLnk.IconLocation = "imageres.dll,27"
$startMenuLnk.Save()

Write-Host "[SUCCESS] Desktop & Start Menu shortcuts created!" -ForegroundColor Green

# 5. Configure Windows Startup Auto-Mount
$startup = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
$startupVbs = Join-Path $startup "Start-CloudSSD.vbs"
$mountScript = Join-Path $InstallDir "scripts\Mount-Disk.ps1"

$vbsContent = @"
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File ""$mountScript"" -Silent", 0, False
Set WshShell = Nothing
"@
Set-Content -Path $startupVbs -Value $vbsContent -Encoding ascii
Write-Host "[SUCCESS] Windows Boot Auto-Start configured!" -ForegroundColor Green

# 6. Register in Windows "Installed apps"
$regKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CloudSSD"
New-Item -Path $regKey -Force | Out-Null
Set-ItemProperty -Path $regKey -Name "DisplayName" -Value "Cloud-SSD (5TB) - Virtual Local Disk"
Set-ItemProperty -Path $regKey -Name "DisplayVersion" -Value "2.0.0"
Set-ItemProperty -Path $regKey -Name "Publisher" -Value "Cloud-SSD Engine"
Set-ItemProperty -Path $regKey -Name "InstallLocation" -Value $InstallDir
Set-ItemProperty -Path $regKey -Name "DisplayIcon" -Value "imageres.dll,27"
Set-ItemProperty -Path $regKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $regKey -Name "NoRepair" -Value 1 -Type DWord
Set-ItemProperty -Path $regKey -Name "UninstallString" -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\Uninstall-CloudSSD.ps1`""

$sizeKb = [int]((Get-ChildItem $InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
Set-ItemProperty -Path $regKey -Name "EstimatedSize" -Value $sizeKb -Type DWord

Write-Host "[SUCCESS] Registered in Windows Installed Apps!" -ForegroundColor Green

# 7. Launch the Installed App
Write-Host "[INFO] Launching Installed Cloud-SSD App..." -ForegroundColor Cyan
Start-Process "wscript.exe" -ArgumentList "`"$vbsPath`""

Write-Host "`n============================================================" -ForegroundColor Cyan
Write-Host "       INSTALLATION COMPLETED SUCCESSFULLY!                " -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
