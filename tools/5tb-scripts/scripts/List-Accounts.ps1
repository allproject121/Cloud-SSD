# Script to list connected accounts and verify storage
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "Common.ps1")

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "            STORAGE & CONNECTED ACCOUNTS OVERVIEW           " -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Cyan

$accounts = Get-ConnectedAccounts
if ($accounts.Count -eq 0) {
    Write-Host "[WARNING] No accounts connected yet!" -ForegroundColor Yellow
    Write-Host "Use option [1] to add your Google Drive accounts." -ForegroundColor White
    Write-Host "`nPress any key to return..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 0
}

Write-Host "Connected Accounts: ($($accounts.Count))`n" -ForegroundColor Green

foreach ($acc in $accounts) {
    Write-Host "Account: $($acc.Name) [$($acc.Type)]" -ForegroundColor Cyan
    try {
        $about = & "$RcloneExe" --config "$ConfigFile" about "$($acc.Name)`:" 2>$null
        if ($about) {
            foreach ($line in $about) {
                Write-Host "   $line" -ForegroundColor Gray
            }
        } else {
            Write-Host "   [Status: Ready (Quota details not returned)]" -ForegroundColor DarkGray
        }
    } catch {
        Write-Host "   [Could not query quota: $_]" -ForegroundColor Red
    }
    Write-Host ""
}

Write-Host "------------------------------------------------------------" -ForegroundColor DarkGray
Write-Host "CloudSSD Union Pool [CloudSSD:]:" -ForegroundColor Yellow
try {
    $poolAbout = & "$RcloneExe" --config "$ConfigFile" about "CloudSSD`:" 2>$null
    if ($poolAbout) {
        foreach ($line in $poolAbout) {
            Write-Host "   $line" -ForegroundColor White
        }
    } else {
        Write-Host "   Pool configured with $($accounts.Count) upstream drives." -ForegroundColor Gray
    }
} catch {
    Write-Host "   Pool configured with $($accounts.Count) upstream drives." -ForegroundColor Gray
}

Write-Host "`nLocal SSD Cache Safety:" -ForegroundColor Yellow
Write-Host "   Location: $CacheDir" -ForegroundColor Gray
Write-Host "   Max SSD Cache Limit: 15 GB" -ForegroundColor Gray
Write-Host "   Min Free Disk Protection: 10 GB" -ForegroundColor Gray

Write-Host "`nPress any key to return..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
