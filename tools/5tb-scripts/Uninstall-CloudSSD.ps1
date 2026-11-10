# Uninstaller for Cloud-SSD (5TB)
$ErrorActionPreference = "SilentlyContinue"

$InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$desktop = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$startMenu = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Programs)
$startup = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)

# 1. Unmount drive
& "$InstallDir\bin\rclone.exe" rc core/quit --rc-addr 127.0.0.1:5572 2>$null
Stop-Process -Name "rclone" -Force 2>$null

# 2. Remove shortcuts
Remove-Item (Join-Path $desktop "Cloud SSD (5TB).lnk") -Force
Remove-Item (Join-Path $startMenu "Cloud-SSD (5TB).lnk") -Force
Remove-Item (Join-Path $startup "Start-CloudSSD.vbs") -Force

# 3. Remove Registry
Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CloudSSD" -Recurse -Force

# 4. Remove installation files (via scheduled batch command after script finishes)
$cmd = "timeout /t 2 /nobreak >nul & rmdir /s /q `"$InstallDir`""
Start-Process cmd.exe -ArgumentList "/c $cmd" -WindowStyle Hidden

[System.Windows.MessageBox]::Show("Cloud-SSD (5TB) has been uninstalled from your PC.`n`nYour files in Google Drive were untouched.", "Uninstall Complete", "OK", "Information")
