# Script to add Google Drive accounts to Cloud-SSD (Single or Multiple Batch Mode)
param(
    [string]$AccountName = ""
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "Common.ps1")

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "       ADD GOOGLE ACCOUNTS TO CLOUD-SSD (MULTI-ACCOUNT)     " -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Cyan

if (-not (Test-Path $RcloneExe)) {
    Write-Host "[ERROR] rclone.exe not found at: $RcloneExe" -ForegroundColor Red
    Pause
    exit 1
}

$adding = $true

while ($adding) {
    $existing = Get-ConnectedAccounts
    Write-Host "`nCurrently connected accounts ($($existing.Count)):" -ForegroundColor Gray
    foreach ($acc in $existing) {
        Write-Host " - $($acc.Name) ($($acc.Type))" -ForegroundColor Green
    }
    Write-Host ""

    # Suggest next drive name (e.g., Drive4, Drive5)
    $nextNum = $existing.Count + 1
    $suggestedName = "Drive$nextNum"

    $name = ""
    if ($AccountName -and $existing.Count -eq 0) {
        $name = $AccountName
    } else {
        $prompt = "Enter account name [Press Enter for default: $suggestedName]"
        $inputName = Read-Host $prompt
        if ([string]::IsNullOrWhiteSpace($inputName)) {
            $name = $suggestedName
        } else {
            $name = ($inputName -replace '[^a-zA-Z0-9_\-]', '_')
        }
    }

    if ($existing | Where-Object { $_.Name -eq $name }) {
        Write-Host "[WARNING] An account named '$name' already exists. Trying with auto-suffix..." -ForegroundColor Yellow
        $name = "${name}_$([System.Environment]::TickCount % 1000)"
    }

    Write-Host "`n[INFO] Starting Google Authentication for '$name'..." -ForegroundColor Cyan
    Write-Host "[INFO] A browser window will open automatically in a few seconds." -ForegroundColor Yellow
    Write-Host "[INFO] Sign in with this Google Account and click 'Allow'." -ForegroundColor White
    Write-Host "[INFO] Waiting for browser authentication...`n" -ForegroundColor Gray

    try {
        & "$RcloneExe" --config "$ConfigFile" config create "$name" drive scope=drive
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "`n[SUCCESS] Account '$name' connected successfully!" -ForegroundColor Green
            Update-CloudSSDUnion
            $all = Get-ConnectedAccounts
            Write-Host "[SUCCESS] CloudSSD 15TB+ pool updated! Total accounts now: $($all.Count)" -ForegroundColor Cyan
        } else {
            Write-Host "`n[ERROR] Authentication failed or was cancelled." -ForegroundColor Red
        }
    } catch {
        Write-Host "`n[ERROR] An unexpected error occurred: $_" -ForegroundColor Red
    }

    # Ask if user wants to add another account right now
    Write-Host "------------------------------------------------------------" -ForegroundColor DarkGray
    $nextChoice = Read-Host "Do you want to add another Google Account right now? (y/n) [default: n]"
    if ($nextChoice -match '^(y|yes)$') {
        $adding = $true
    } else {
        $adding = $false
    }
}

Write-Host "`nAll done! Pool refreshed." -ForegroundColor Green
Write-Host "Press any key to return..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
