# Script to remove a connected account from Cloud-SSD
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "Common.ps1")

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "                REMOVE A CONNECTED ACCOUNT                  " -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Cyan

$accounts = Get-ConnectedAccounts
if ($accounts.Count -eq 0) {
    Write-Host "No accounts are currently configured." -ForegroundColor Yellow
    Write-Host "`nPress any key to return..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 0
}

Write-Host "Select an account to remove:" -ForegroundColor White
for ($i = 0; $i -lt $accounts.Count; $i++) {
    Write-Host " [$($i + 1)] $($accounts[$i].Name) ($($accounts[$i].Type))" -ForegroundColor Cyan
}
Write-Host " [0] Cancel" -ForegroundColor Gray

$choice = Read-Host "`nEnter number"
if ($choice -match '^\d+$') {
    $idx = [int]$choice
    if ($idx -ge 1 -and $idx -le $accounts.Count) {
        $selected = $accounts[$idx - 1].Name
        $confirm = Read-Host "Are you sure you want to remove '$selected'? (y/n)"
        if ($confirm -eq 'y' -or $confirm -eq 'Y') {
            & "$RcloneExe" --config "$ConfigFile" config delete "$selected"
            Update-CloudSSDUnion
            Write-Host "`n[SUCCESS] '$selected' removed successfully and pool updated." -ForegroundColor Green
        } else {
            Write-Host "`nOperation cancelled." -ForegroundColor Gray
        }
    }
}

Write-Host "`nPress any key to return..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
