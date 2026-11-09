<#
.SYNOPSIS
  Installs Cloud-SSD for the current Windows user (no administrator rights needed).
.DESCRIPTION
  Copies the application to %LOCALAPPDATA%\Programs\Cloud-SSD, creates a Start menu shortcut (and optionally a
  desktop shortcut), and registers an uninstall entry under "Installed apps". WinFsp is required for the virtual
  drive; if it is missing this script tells you where to get it but does not install it silently.
.PARAMETER DesktopShortcut
  Also create a desktop shortcut.
.PARAMETER InstallDir
  Custom installation folder.
#>
param(
    [switch]$DesktopShortcut,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\Cloud-SSD"),
    [switch]$Quiet
)
$ErrorActionPreference = "Stop"
$src = $PSScriptRoot
if (-not (Test-Path (Join-Path $src "Cloud-SSD.exe")) -and -not (Test-Path (Join-Path $src "CloudPlayX.exe"))) { throw "Run this script from the extracted Cloud-SSD release folder." }

$running = Get-Process Cloud-SSD, CloudPlayX, CloudSSD -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, 'OrdinalIgnoreCase') }
if ($running) { throw "Cloud-SSD is running from $InstallDir. Close it first." }

Write-Host "Installing Cloud-SSD to $InstallDir ..."
New-Item -ItemType Directory -Force $InstallDir | Out-Null
# Replace program files only; user data lives in %LOCALAPPDATA%\Cloud-SSD and is never touched here.
Get-ChildItem $src -Force | Where-Object { $_.Name -notin @("Install.cmd") } | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $InstallDir $_.Name) -Recurse -Force
}

$exeName = if (Test-Path (Join-Path $InstallDir "Cloud-SSD.exe")) { "Cloud-SSD.exe" } else { "CloudPlayX.exe" }
$exe = Join-Path $InstallDir $exeName
$shell = New-Object -ComObject WScript.Shell
function New-Shortcut($path) {
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $InstallDir
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = "Cloud-SSD - Cloud-to-SSD virtual drive and storage engine"
    $lnk.Save()
}
$startMenu = Join-Path ([Environment]::GetFolderPath("Programs")) "Cloud-SSD.lnk"
New-Shortcut $startMenu
if ($DesktopShortcut) { New-Shortcut (Join-Path ([Environment]::GetFolderPath("Desktop")) "Cloud-SSD.lnk") }

# Uninstall entry (per user).
$key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CloudSSD"
New-Item -Path $key -Force | Out-Null
$version = (Get-Item $exe).VersionInfo.ProductVersion
Set-ItemProperty $key DisplayName "Cloud-SSD"
Set-ItemProperty $key DisplayVersion $version
Set-ItemProperty $key Publisher "Cloud-SSD"
Set-ItemProperty $key InstallLocation $InstallDir
Set-ItemProperty $key DisplayIcon "$exe,0"
Set-ItemProperty $key NoModify 1 -Type DWord
Set-ItemProperty $key NoRepair 1 -Type DWord
Set-ItemProperty $key UninstallString "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\Uninstall-CloudPlayX.ps1`""
$size = [int]((Get-ChildItem $InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
Set-ItemProperty $key EstimatedSize $size -Type DWord

# Prerequisite check (informational only).
$winfsp = Test-Path "${env:ProgramFiles(x86)}\WinFsp\bin\winfsp-x64.dll"
Write-Host ""
Write-Host "Cloud-SSD $version installed." -ForegroundColor Green
if (-not $winfsp) {
    Write-Host "WinFsp is NOT installed. The virtual drive needs it." -ForegroundColor Yellow
    Write-Host "  Download the official installer from https://winfsp.dev/rel/  (or run: winget install WinFsp.WinFsp)"
} else { Write-Host "WinFsp: found." }
Write-Host "Start Cloud-SSD from the Start menu."
if (-not $Quiet) { Start-Process $exe }
