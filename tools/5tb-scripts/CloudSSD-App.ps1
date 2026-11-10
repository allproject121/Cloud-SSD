# Cloud-SSD Native GUI Application (WPF) - Latest Edition
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ScriptsFolder = Join-Path $ScriptDir "scripts"
. (Join-Path $ScriptsFolder "Common.ps1")

[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Cloud-SSD (5TB) - Control Center"
        Height="620" Width="780"
        WindowStartupLocation="CenterScreen"
        ResizeMode="CanMinimize"
        Background="#0F172A"
        Foreground="#F8FAFC"
        FontFamily="Segoe UI">
    <Window.Resources>
        <Style TargetType="Button">
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Padding" Value="16,10"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Foreground" Value="#FFFFFF"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Grid Margin="28">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <Grid Grid.Row="0" Margin="0,0,0,22">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="⚡ Cloud-SSD" FontSize="26" FontWeight="Bold" Foreground="#38BDF8"/>
                    <Border Background="#1E293B" CornerRadius="6" Padding="8,2" Margin="10,4,0,0" VerticalAlignment="Center">
                        <TextBlock Text="v2.0 LATEST" FontSize="11" FontWeight="Bold" Foreground="#0284C7"/>
                    </Border>
                </StackPanel>
                <TextBlock Text="Virtual Local Disk Pool - Multi-Account Storage for Windows" FontSize="13" Foreground="#94A3B8" Margin="0,4,0,0"/>
            </StackPanel>
            
            <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                <Button x:Name="BtnRefresh" Content="🔄 Refresh" Background="#1E293B" Foreground="#94A3B8" Padding="10,6" Margin="0,0,10,0" FontSize="12"/>
                <Border x:Name="BadgeStatus" Background="#064E3B" CornerRadius="20" Padding="16,8">
                    <TextBlock x:Name="TxtStatus" Text="● Drive Z:\ is Connected" FontWeight="Bold" FontSize="13" Foreground="#34D399"/>
                </Border>
            </StackPanel>
        </Grid>

        <!-- Main Storage Card -->
        <Border Grid.Row="1" Background="#1E293B" CornerRadius="12" Padding="22" Margin="0,0,0,18">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <Grid Grid.Row="0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <StackPanel Grid.Column="0">
                        <TextBlock Text="POOLED CLOUD CAPACITY (ALL ACCOUNTS COMBINED)" FontSize="11" FontWeight="Bold" Foreground="#64748B"/>
                        <TextBlock x:Name="TxtPoolSize" Text="15,311 GB Free (15+ Terabytes)" FontSize="24" FontWeight="Bold" Foreground="#F8FAFC" Margin="0,4,0,0"/>
                        <TextBlock Text="Files automatically save to whichever Google account has the most free space." FontSize="12" Foreground="#94A3B8" Margin="0,4,0,0"/>
                    </StackPanel>
                    <Border Grid.Column="1" Background="#0F172A" CornerRadius="8" Padding="14,10" VerticalAlignment="Center">
                        <StackPanel HorizontalAlignment="Center">
                            <TextBlock Text="DRIVE LETTER" FontSize="10" FontWeight="Bold" Foreground="#64748B" HorizontalAlignment="Center"/>
                            <TextBlock Text="Z:\" FontSize="20" FontWeight="Bold" Foreground="#38BDF8" HorizontalAlignment="Center"/>
                        </StackPanel>
                    </Border>
                </Grid>

                <!-- Action Buttons -->
                <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,20,0,0">
                    <Button x:Name="BtnOpenDrive" Content="📂  Open Z:\ Drive in Explorer" Background="#0284C7" Margin="0,0,12,0"/>
                    <Button x:Name="BtnMount" Content="⚡  Connect Drive" Background="#059669" Margin="0,0,12,0"/>
                    <Button x:Name="BtnUnmount" Content="🛑  Disconnect" Background="#DC2626" Margin="0,0,0,0"/>
                </StackPanel>
            </Grid>
        </Border>

        <!-- Bottom 2 Columns: Accounts & Info -->
        <Grid Grid.Row="2" Margin="0,0,0,16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="18"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <!-- Accounts Card -->
            <Border Grid.Column="0" Background="#1E293B" CornerRadius="12" Padding="20">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    
                    <Grid Grid.Row="0" Margin="0,0,0,12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="CONNECTED GOOGLE ACCOUNTS" FontSize="11" FontWeight="Bold" Foreground="#64748B"/>
                        <TextBlock x:Name="TxtAccountCount" Grid.Column="1" Text="3 Active" FontSize="11" FontWeight="Bold" Foreground="#34D399"/>
                    </Grid>

                    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
                        <StackPanel x:Name="StackAccounts">
                            <!-- Populated dynamically in script -->
                        </StackPanel>
                    </ScrollViewer>

                    <Button x:Name="BtnAddAccount" Grid.Row="2" Content="➕  Add Google Account (Batch Mode)" Background="#334155" Margin="0,14,0,0" HorizontalAlignment="Stretch"/>
                </Grid>
            </Border>

            <!-- Features & Safety Card -->
            <Border Grid.Column="2" Background="#1E293B" CornerRadius="12" Padding="20">
                <StackPanel>
                    <TextBlock Text="SYSTEM SAFETY &amp; PERFORMANCE" FontSize="11" FontWeight="Bold" Foreground="#64748B" Margin="0,0,0,14"/>
                    
                    <Border Background="#0F172A" CornerRadius="8" Padding="12" Margin="0,0,0,10">
                        <StackPanel>
                            <TextBlock Text="🛡️ Laptop SSD Protection" FontWeight="SemiBold" FontSize="13" Foreground="#F8FAFC"/>
                            <TextBlock Text="Strict 15 GB cache cap on SSD. Windows C: drive will never run out of storage." FontSize="11" Foreground="#94A3B8" Margin="0,3,0,0" TextWrapping="Wrap"/>
                        </StackPanel>
                    </Border>

                    <Border Background="#0F172A" CornerRadius="8" Padding="12" Margin="0,0,0,10">
                        <StackPanel>
                            <TextBlock Text="⚡ Turbo Acceleration Engine" FontWeight="SemiBold" FontSize="13" Foreground="#F8FAFC"/>
                            <TextBlock Text="4 parallel read streams + 64MB upload chunks active for high transfer speed." FontSize="11" Foreground="#94A3B8" Margin="0,3,0,0" TextWrapping="Wrap"/>
                        </StackPanel>
                    </Border>

                    <Border Background="#0F172A" CornerRadius="8" Padding="12" Margin="0,0,0,0">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <StackPanel Grid.Column="0">
                                <TextBlock Text="🚀 Auto-Start on Boot" FontWeight="SemiBold" FontSize="13" Foreground="#F8FAFC"/>
                                <TextBlock Text="Z:\ connects quietly when laptop boots" FontSize="11" Foreground="#94A3B8" Margin="0,2,0,0"/>
                            </StackPanel>
                            <Button x:Name="BtnToggleAutoStart" Grid.Column="1" Content="Enabled" Background="#059669" FontSize="11" Padding="12,5" VerticalAlignment="Center"/>
                        </Grid>
                    </Border>
                </StackPanel>
            </Border>
        </Grid>

        <!-- Status Footer -->
        <TextBlock x:Name="TxtFooter" Grid.Row="3" Text="Ready. Click 'Open Z:\ Drive in Explorer' to access all your files." FontSize="12" Foreground="#64748B" VerticalAlignment="Center"/>
    </Grid>
</Window>
"@

$reader = (New-Object System.Xml.XmlNodeReader $xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)

# Get element references
$badgeStatus = $window.FindName("BadgeStatus")
$txtStatus = $window.FindName("TxtStatus")
$txtPoolSize = $window.FindName("TxtPoolSize")
$txtAccountCount = $window.FindName("TxtAccountCount")
$btnRefresh = $window.FindName("BtnRefresh")
$btnOpenDrive = $window.FindName("BtnOpenDrive")
$btnMount = $window.FindName("BtnMount")
$btnUnmount = $window.FindName("BtnUnmount")
$btnAddAccount = $window.FindName("BtnAddAccount")
$stackAccounts = $window.FindName("StackAccounts")
$btnToggleAutoStart = $window.FindName("BtnToggleAutoStart")
$txtFooter = $window.FindName("TxtFooter")

function Refresh-UI {
    $isMounted = Is-DriveMounted $DefaultDriveLetter
    if ($isMounted) {
        $badgeStatus.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#064E3B")
        $txtStatus.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#34D399")
        $txtStatus.Text = "● Drive Z:\ is Connected"
        $btnMount.IsEnabled = $false
        $btnMount.Opacity = 0.4
        $btnUnmount.IsEnabled = $true
        $btnUnmount.Opacity = 1.0
        $btnOpenDrive.IsEnabled = $true
        $btnOpenDrive.Opacity = 1.0
    } else {
        $badgeStatus.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#450A0A")
        $txtStatus.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#F87171")
        $txtStatus.Text = "○ Drive Z:\ is Disconnected"
        $btnMount.IsEnabled = $true
        $btnMount.Opacity = 1.0
        $btnUnmount.IsEnabled = $false
        $btnUnmount.Opacity = 0.4
        $btnOpenDrive.IsEnabled = $false
        $btnOpenDrive.Opacity = 0.4
    }

    # Populate accounts
    $stackAccounts.Children.Clear()
    $accounts = Get-ConnectedAccounts
    $txtAccountCount.Text = "$($accounts.Count) Active"

    if ($accounts.Count -eq 0) {
        $tb = New-Object System.Windows.Controls.TextBlock
        $tb.Text = "No accounts connected yet."
        $tb.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#64748B")
        $tb.FontSize = 12
        $stackAccounts.Children.Add($tb) | Out-Null
    } else {
        foreach ($acc in $accounts) {
            $border = New-Object System.Windows.Controls.Border
            $border.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#0F172A")
            $border.CornerRadius = [System.Windows.CornerRadius]::new(8)
            $border.Padding = [System.Windows.Thickness]::new(12, 10, 12, 10)
            $border.Margin = [System.Windows.Thickness]::new(0, 0, 0, 8)

            $sp = New-Object System.Windows.Controls.StackPanel
            $sp.Orientation = [System.Windows.Controls.Orientation]::Horizontal

            $icon = New-Object System.Windows.Controls.TextBlock
            $icon.Text = "● "
            $icon.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#10B981")
            $icon.FontWeight = [System.Windows.FontWeights]::Bold
            $icon.Margin = [System.Windows.Thickness]::new(0, 0, 6, 0)

            $name = New-Object System.Windows.Controls.TextBlock
            $name.Text = "$($acc.Name) "
            $name.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#F8FAFC")
            $name.FontWeight = [System.Windows.FontWeights]::SemiBold

            $type = New-Object System.Windows.Controls.TextBlock
            $type.Text = "(Google Drive)"
            $type.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#64748B")
            $type.FontSize = 12

            $sp.Children.Add($icon) | Out-Null
            $sp.Children.Add($name) | Out-Null
            $sp.Children.Add($type) | Out-Null
            $border.Child = $sp
            $stackAccounts.Children.Add($border) | Out-Null
        }
    }

    # Check auto-start
    $startupFolder = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
    $vbsPath = Join-Path $startupFolder "Start-CloudSSD.vbs"
    if (Test-Path $vbsPath) {
        $btnToggleAutoStart.Content = "Enabled"
        $btnToggleAutoStart.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#059669")
    } else {
        $btnToggleAutoStart.Content = "Disabled"
        $btnToggleAutoStart.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#475569")
    }
}

# Button click events
$btnRefresh.Add_Click({
    Refresh-UI
    $txtFooter.Text = "Status refreshed at $(Get-Date -Format 'HH:mm:ss')"
})

$btnOpenDrive.Add_Click({
    if (Is-DriveMounted $DefaultDriveLetter) {
        Start-Process explorer.exe "$DefaultDriveLetter\"
        $txtFooter.Text = "Opened File Explorer to $DefaultDriveLetter\"
    } else {
        $txtFooter.Text = "Drive is not connected. Click 'Connect Drive' first."
    }
})

$btnMount.Add_Click({
    $txtFooter.Text = "Connecting Drive $DefaultDriveLetter... Please wait..."
    & (Join-Path $ScriptsFolder "Mount-Disk.ps1") -Silent
    Start-Sleep -Seconds 2
    Refresh-UI
    $txtFooter.Text = "Drive $DefaultDriveLetter connected successfully!"
})

$btnUnmount.Add_Click({
    $txtFooter.Text = "Disconnecting Drive $DefaultDriveLetter..."
    & (Join-Path $ScriptsFolder "Unmount-Disk.ps1") -Silent
    Start-Sleep -Seconds 1
    Refresh-UI
    $txtFooter.Text = "Drive $DefaultDriveLetter disconnected."
})

$btnAddAccount.Add_Click({
    $window.Hide()
    & (Join-Path $ScriptsFolder "Add-Account.ps1")
    $window.Show()
    Refresh-UI
    $txtFooter.Text = "Accounts list updated."
})

$btnToggleAutoStart.Add_Click({
    $startupFolder = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
    $vbsPath = Join-Path $startupFolder "Start-CloudSSD.vbs"
    if (Test-Path $vbsPath) {
        & (Join-Path $ScriptsFolder "Setup-AutoStart.ps1") -Disable
    } else {
        & (Join-Path $ScriptsFolder "Setup-AutoStart.ps1")
    }
    Refresh-UI
})

$window.Add_Loaded({
    $window.Activate()
    $window.Topmost = $true
    $window.Topmost = $false
})

Refresh-UI
$window.ShowDialog() | Out-Null
