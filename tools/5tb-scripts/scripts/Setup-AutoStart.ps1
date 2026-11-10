# Script to configure silent Auto-Start on Windows logon
param(
    [switch]$Disable
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "Common.ps1")

$startupFolder = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
$vbsPath = Join-Path $startupFolder "Start-CloudSSD.vbs"
$mountScript = Join-Path $ScriptDir "Mount-Disk.ps1"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "          CONFIGURE AUTO-START ON WINDOWS LOGON             " -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Cyan

if ($Disable) {
    if (Test-Path $vbsPath) {
        Remove-Item $vbsPath -Force
        Write-Host "[SUCCESS] Auto-start disabled. Startup file removed." -ForegroundColor Green
    } else {
        Write-Host "[INFO] Auto-start was not enabled." -ForegroundColor Yellow
    }
} else {
    $vbsContent = @"
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File ""$mountScript"" -Silent", 0, False
Set WshShell = Nothing
"@
    Set-Content -Path $vbsPath -Value $vbsContent -Encoding ascii
    Write-Host "[SUCCESS] Auto-start enabled successfully!" -ForegroundColor Green
    Write-Host "Whenever your laptop boots or you log in to Windows," -ForegroundColor White
    Write-Host "Cloud-SSD (Z:) will automatically connect quietly in the background." -ForegroundColor White
    Write-Host "Startup file created at: $vbsPath" -ForegroundColor DarkGray
}

Write-Host "`nPress any key to return..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
