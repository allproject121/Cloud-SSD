using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CloudSSD.App;
using CloudSSD.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudSSD.Desktop.ViewModels;

public sealed partial class MobileHubViewModel : PageViewModel
{
    public MobileHubViewModel(AppHost host) : base(host, "Phone (Mobile Hub)", "\uE8EA")
    {
        UploadDirectory = host.Settings.MobileBridge.UploadDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Cloud Drive_Uploads");

        PhotosBackupDirectory = host.Settings.MobileBridge.PhotoBackupDirectory
            ?? Path.Combine(host.Paths.DataRoot, "Mobile_Backups", "Photos");

        _allowDriveBrowsing = host.Settings.MobileBridge.AllowDriveBrowsing;
        _allowStreaming = host.Settings.MobileBridge.AllowStreaming;
        _allowUploads = host.Settings.MobileBridge.AllowUploads;
        _allowGamingHud = host.Settings.MobileBridge.AllowGamingHud;
        _allowFileManagement = host.Settings.MobileBridge.AllowFileManagement;
        _allowPcControl = host.Settings.MobileBridge.AllowPcControl;
        _allowPcDisks = host.Settings.MobileBridge.AllowPcDisks;
        _allowTailscale = host.Settings.MobileBridge.AllowTailscale;
        _tailscaleAuto = host.Settings.MobileBridge.TailscaleAuto;

        Host.MobileBridge.StatusChanged += () => Ui(RefreshState);
        Host.MobileBridge.FileTransferred += (name, bytes) => Ui(() =>
        {
            RecentTransfers.Insert(0, $"• {name} ({ByteSize.Format(bytes)}) at {DateTime.Now:HH:mm:ss}");
            if (RecentTransfers.Count > 10) RecentTransfers.RemoveAt(RecentTransfers.Count - 1);
        });
        Host.MobileBridge.TransferRecorded += record => Ui(() => OnTransferRecorded(record));

        RefreshState();
        LoadTransferHistory();
        Host.Offloader.OffloadCompleted += _ => Ui(RefreshState);
    }

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "Bridge Stopped";
    [ObservableProperty] private string _statusTone = "warn";
    [ObservableProperty] private string _localIp = "127.0.0.1";
    [ObservableProperty] private int _port = 8090;
    [ObservableProperty] private string _localUrl = "http://127.0.0.1:8090/";
    [ObservableProperty] private string _pairingUrl = "";
    [ObservableProperty] private string _pairingCode = "";
    [ObservableProperty] private bool _allowFileManagement;
    [ObservableProperty] private bool _allowPcControl;
    [ObservableProperty] private bool _allowPcDisks;
    [ObservableProperty] private bool _allowTailscale;
    [ObservableProperty] private int _legacyPhones;
    [ObservableProperty] private bool _legacyKeyOn;
    [ObservableProperty] private string _tailscaleText = "";

    /// <summary>Lets phones reach the bridge through Tailscale on any network profile (run once, as administrator).</summary>
    public string FirewallCommand => "netsh " + TailscaleSetup.FirewallArguments(Host.MobileBridge.Port);

    [ObservableProperty] private bool _tailscaleAuto;
    [ObservableProperty] private string _awaySetupText = "";
    private bool _settingUpAway;

    partial void OnTailscaleAutoChanged(bool value)
    {
        Host.Settings.MobileBridge.TailscaleAuto = value;
        Host.SaveSettings();
    }

    /// <summary>
    /// One button for "away from home": installs Tailscale when it is missing, opens its sign-in, then lets phones
    /// in through it and adds the firewall rule (Windows asks once). Only the sign-in needs the user.
    /// </summary>
    [RelayCommand]
    private async Task SetUpAwayAsync()
    {
        if (_settingUpAway) return;
        _settingUpAway = true;
        try
        {
            if (TailscaleSetup.FindExe() == null)
            {
                AwaySetupText = L("Installing Tailscale… (Windows may ask for permission)");
                var (ok, message) = await TailscaleSetup.InstallAsync();
                if (!ok) { AwaySetupText = L(message); return; }
            }
            if (await Task.Run(TailscaleSetup.Read) != TailscaleSetup.State.Running)
            {
                AwaySetupText = L("Sign in to Tailscale in the browser that opened (once). Waiting…");
                if (!await TailscaleSetup.LoginAsync())
                {
                    AwaySetupText = L("Tailscale is not signed in yet. Finish the sign-in, then press the button again.");
                    return;
                }
            }
            AllowTailscale = true;
            var b = Host.Settings.MobileBridge;
            if (!await Task.Run(TailscaleSetup.FirewallRuleExists))
            {
                AwaySetupText = L("Allowing phones through the firewall… (answer Yes in the Windows prompt)");
                b.TailscaleFirewallAsked = true;
                Host.SaveSettings();
                if (!await Task.Run(() => TailscaleSetup.AddFirewallRule(Host.MobileBridge.Port)))
                {
                    AwaySetupText = L("Tailscale works, but the firewall rule was not added. Run the command below once as administrator.");
                    RefreshState();
                    return;
                }
            }
            TailscaleInfo.Forget();
            RefreshState();
            AwaySetupText = L("Done. Install Tailscale on the phone and sign in with the same account; the app switches to it by itself away from home.");
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            AwaySetupText = e.Message;
        }
        finally { _settingUpAway = false; }
    }

    private static string L(string text) => LocalizationManager.T(text);

    private string _shownLinkSecret = "";
    [ObservableProperty] private BitmapSource? _qrCode;
    [ObservableProperty] private string _transferRateFormatted = "0.0 MB/s";
    [ObservableProperty] private string _uploadDirectory;
    [ObservableProperty] private string _photosBackupDirectory;
    [ObservableProperty] private bool _allowDriveBrowsing;
    [ObservableProperty] private bool _allowStreaming;
    [ObservableProperty] private bool _allowUploads;
    [ObservableProperty] private bool _allowGamingHud;

    [ObservableProperty] private string _selectedFilter = "All";
    [ObservableProperty] private string _transferSearchText = "";
    [ObservableProperty] private int _totalTransferCount;
    [ObservableProperty] private string _storageSaverSummary = "Storage Saver: Active";


    public ObservableCollection<PairedDeviceItem> PairedDevices { get; } = new();
    public ObservableCollection<string> RecentTransfers { get; } = new();
    public ObservableCollection<TransferRecordViewModel> AllTransfers { get; } = new();
    public ObservableCollection<TransferRecordViewModel> FilteredTransfers { get; } = new();

    public override void OnShown()
    {
        base.OnShown();
        // The automatic Tailscale set-up may have switched it on meanwhile.
        if (AllowTailscale != Host.Settings.MobileBridge.AllowTailscale) AllowTailscale = Host.Settings.MobileBridge.AllowTailscale;
        RefreshState();
    }

    public override void OnMetrics(EngineMetrics m)
    {
        base.OnMetrics(m);
        double rateMbps = Host.MobileBridge.CurrentTransferRateMbps;
        TransferRateFormatted = rateMbps > 0 ? $"{rateMbps / 8.0:0.0} MB/s ({rateMbps:0.0} Mbps)" : "0.0 MB/s";
        // The QR link lasts ten minutes; show the new one when it has been replaced.
        if (Host.MobileBridge.PairingLinkSecret != _shownLinkSecret) RefreshState();
    }

    private void RefreshState()
    {
        IsRunning = Host.MobileBridge.IsRunning;
        StatusText = IsRunning ? "Local Bridge Running" : "Local Bridge Stopped";
        StatusTone = IsRunning ? "ok" : "warn";
        LocalIp = Host.MobileBridge.LocalIpAddress;
        Port = Host.MobileBridge.Port;
        LocalUrl = Host.MobileBridge.LocalUrl;
        // The QR code carries a one-time secret (ten minutes, home network only), never a lasting key.
        _shownLinkSecret = Host.MobileBridge.PairingLinkSecret;
        PairingUrl = $"{LocalUrl}?pair={_shownLinkSecret}";
        LegacyPhones = Host.MobileBridge.LegacyPhones;
        LegacyKeyOn = Host.MobileBridge.LegacyTokenAccepted;
        var ts = Host.Settings.MobileBridge.AllowTailscale ? TailscaleInfo.Current() : null;
        TailscaleText = ts is null ? "Off. Phones work only on the same Wi-Fi as this PC."
            : ts.Available ? $"On. Away from home the phone uses {string.Join(" or ", ts.Ips.Take(1).Concat(ts.DnsName is { } n ? new[] { n } : []))} (Tailscale must be on in the phone)."
            : "On, but Tailscale is not running on this PC. Install or start Tailscale and sign in.";
        var code = Host.MobileBridge.PairingCode;
        PairingCode = $"{code[..3]} {code[3..]}"; // "123 456" is easier to read off the screen

        try
        {
            QrCode = QrCodeGenerator.GenerateQrBitmap(PairingUrl, 8);
        }
        catch
        {
            QrCode = null;
        }

        var off = Host.Offloader.GetStatus();
        StorageSaverSummary = $"Laptop: {off.LaptopFreeSpaceGB} GB free • Threshold: {off.MinFreeSpaceThresholdGB} GB (Auto-shift to {off.TargetDriveLetter}:)";

        PairedDevices.Clear();
        foreach (var d in Host.Settings.MobileBridge.PairedDevices)
        {
            PairedDevices.Add(new PairedDeviceItem(d, this));
        }
    }

    [RelayCommand]
    private void ToggleServer()
    {
        if (Host.MobileBridge.IsRunning)
        {
            Host.MobileBridge.Stop();
            Host.Settings.MobileBridge.Enabled = false;
        }
        else
        {
            Host.MobileBridge.Start();
            Host.Settings.MobileBridge.Enabled = true;
        }
        Host.SaveSettings();
        RefreshState();
    }

    [RelayCommand]
    private void RegenerateToken()
    {
        // Only the six digits and the QR link change; phones that are already paired keep working.
        Host.MobileBridge.NewPairingSecrets();
        RefreshState();
        Say("New pairing code and QR code made. Phones that are already paired keep working.", "ok");
    }

    [RelayCommand]
    private void RetireLegacyKey()
    {
        if (!Confirm("Turn off the old shared key?\n\nPhones paired before this update (marked 'old key') stop working until they are paired again with the code or QR. Updated phone apps switch to their own key by themselves.", "Cloud Drive Mobile Hub")) return;
        Host.MobileBridge.RetireLegacyToken();
        RefreshState();
        Say("Old shared key turned off.", "ok");
    }

    [RelayCommand]
    private void CopyFirewallCommand()
    {
        try
        {
            Clipboard.SetText(FirewallCommand);
            Say("Command copied. Paste it into an administrator PowerShell or Command Prompt once.", "ok");
        }
        catch (Exception ex)
        {
            Say("Could not copy to clipboard: " + ex.Message, "warn");
        }
    }

    [RelayCommand]
    private void CopyUrl()
    {
        try
        {
            Clipboard.SetText(PairingUrl);
            Say("Pairing link copied to clipboard!", "ok");
        }
        catch (Exception ex)
        {
            Say("Could not copy to clipboard: " + ex.Message, "warn");
        }
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(PairingUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Say("Could not open browser: " + ex.Message, "error");
        }
    }

    [RelayCommand]
    private void BrowseUploads()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Folder for Phone Uploads",
            InitialDirectory = UploadDirectory
        };
        if (dlg.ShowDialog() == true)
        {
            UploadDirectory = dlg.FolderName;
            Host.Settings.MobileBridge.UploadDirectory = dlg.FolderName;
            Host.SaveSettings();
        }
    }

    [RelayCommand]
    private void BrowsePhotos()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Folder for Phone Photos Backup",
            InitialDirectory = PhotosBackupDirectory
        };
        if (dlg.ShowDialog() == true)
        {
            PhotosBackupDirectory = dlg.FolderName;
            Host.Settings.MobileBridge.PhotoBackupDirectory = dlg.FolderName;
            Host.SaveSettings();
        }
    }

    [RelayCommand]
    private void RevokeAll()
    {
        if (!Confirm("Are you sure you want to disconnect all paired mobile devices?", "Cloud Drive Mobile Hub")) return;
        // Removes every phone's key and the old shared key, and makes a new pairing code: nothing paired before works.
        Host.MobileBridge.RevokeAll();
        RefreshState();
        Say("All paired mobile devices have been disconnected.", "ok");
    }

    partial void OnAllowFileManagementChanged(bool value)
    {
        Host.Settings.MobileBridge.AllowFileManagement = value;
        Host.SaveSettings();
    }

    partial void OnAllowPcControlChanged(bool value)
    {
        Host.Settings.MobileBridge.AllowPcControl = value;
        Host.SaveSettings();
    }

    partial void OnAllowPcDisksChanged(bool value)
    {
        Host.Settings.MobileBridge.AllowPcDisks = value;
        Host.SaveSettings();
    }

    partial void OnAllowTailscaleChanged(bool value)
    {
        Host.Settings.MobileBridge.AllowTailscale = value;
        Host.SaveSettings();
        RefreshState();
    }

    partial void OnAllowDriveBrowsingChanged(bool value)
    {
        Host.Settings.MobileBridge.AllowDriveBrowsing = value;
        Host.SaveSettings();
    }

    partial void OnAllowStreamingChanged(bool value)
    {
        Host.Settings.MobileBridge.AllowStreaming = value;
        Host.SaveSettings();
    }

    partial void OnAllowUploadsChanged(bool value)
    {
        Host.Settings.MobileBridge.AllowUploads = value;
        Host.SaveSettings();
    }

    partial void OnAllowGamingHudChanged(bool value)
    {
        Host.Settings.MobileBridge.AllowGamingHud = value;
        Host.SaveSettings();
    }

    public void RemoveDevice(PairedDeviceItem item)
    {
        Host.MobileBridge.RemoveDevice(item.Id); // its key stops working at once
        RefreshState();
        Say($"Device '{item.Name}' removed.", "ok");
    }

    [RelayCommand]
    private async Task OffloadToCloudNowAsync()
    {
        Say("Starting Smart Storage Offload to Cloud Drive…", "info");
        var res = await Host.Offloader.RunOffloadAsync(manual: true);
        Say(res.Message, res.Success ? "ok" : "warn");
        RefreshState();
    }

    partial void OnSelectedFilterChanged(string value) => ApplyTransferFilter();
    partial void OnTransferSearchTextChanged(string value) => ApplyTransferFilter();

    [RelayCommand]
    private void SetFilter(string filter)
    {
        SelectedFilter = filter;
    }

    private void OnTransferRecorded(TransferRecord record)
    {
        var vm = new TransferRecordViewModel(record, this);
        AllTransfers.Insert(0, vm);
        if (AllTransfers.Count > 200) AllTransfers.RemoveAt(AllTransfers.Count - 1);
        TotalTransferCount = AllTransfers.Count;
        ApplyTransferFilter();
    }

    private void LoadTransferHistory()
    {
        AllTransfers.Clear();
        foreach (var rec in Host.MobileBridge.GetTransferHistory())
        {
            AllTransfers.Add(new TransferRecordViewModel(rec, this));
        }
        TotalTransferCount = AllTransfers.Count;
        ApplyTransferFilter();
    }

    private void ApplyTransferFilter()
    {
        FilteredTransfers.Clear();
        string filter = SelectedFilter ?? "All";
        string search = (TransferSearchText ?? "").Trim();

        foreach (var item in AllTransfers)
        {
            if (filter == "Received" && item.Direction != "upload") continue;
            if (filter == "Sent" && item.Direction != "download") continue;
            if (filter == "Streamed" && item.Direction != "stream") continue;
            if (filter == "Offload" && item.Direction != "offload") continue;

            if (!string.IsNullOrEmpty(search))
            {
                if (!item.FileName.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                    !item.DeviceName.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            FilteredTransfers.Add(item);
        }
    }

    [RelayCommand]
    private void ClearTransferHistory()
    {
        if (AllTransfers.Count == 0) return;
        if (!Confirm("Are you sure you want to clear the transfer history?", "Cloud-SSD Transfer History")) return;

        Host.MobileBridge.ClearTransferHistory();
        AllTransfers.Clear();
        FilteredTransfers.Clear();
        RecentTransfers.Clear();
        TotalTransferCount = 0;
        Say("Transfer history cleared.", "ok");
    }

    [RelayCommand]
    private void OpenUploadsFolder()
    {
        try
        {
            if (!Directory.Exists(UploadDirectory)) Directory.CreateDirectory(UploadDirectory);
            Process.Start("explorer.exe", "\"" + UploadDirectory + "\"");
        }
        catch (Exception ex)
        {
            Say("Cannot open uploads directory: " + ex.Message, "error");
        }
    }
    public void NotifyUser(string message, string tone = "info") => Say(message, tone);
}
public sealed partial class TransferRecordViewModel : ObservableObject
{
    private readonly MobileHubViewModel _parent;

    public TransferRecordViewModel(TransferRecord model, MobileHubViewModel parent)
    {
        _parent = parent;
        Model = model;
        Id = model.Id;
        FileName = model.FileName;
        FilePath = model.FilePath;
        Bytes = model.Bytes;
        SizeFormatted = string.IsNullOrEmpty(model.SizeFormatted) ? ByteSize.Format(model.Bytes) : model.SizeFormatted;
        Direction = model.Direction;
        DeviceName = model.DeviceName;
        DeviceIp = model.DeviceIp;
        Timestamp = model.Timestamp;
        SpeedMBps = model.SpeedMBps;
        Status = model.Status;

        if (model.Direction == "download")
        {
            DirectionLabel = "Sent to Phone";
            DirectionBadge = "↑ Sent";
            DirectionTone = "accent";
            DirectionIcon = "";
        }
        else if (model.Direction == "upload")
        {
            DirectionLabel = "Received from Phone";
            DirectionBadge = "↓ Received";
            DirectionTone = "ok";
            DirectionIcon = "";
        }
        else if (model.Direction == "offload")
        {
            DirectionLabel = "Moved to the cloud (Storage Saver)";
            DirectionBadge = "☁ Space saver";
            DirectionTone = "accent";
            DirectionIcon = "";
        }
        else
        {
            DirectionLabel = "Streamed to Phone";
            DirectionBadge = "► Streamed";
            DirectionTone = "purple";
            DirectionIcon = "";
        }

        SpeedFormatted = model.SpeedMBps > 0 ? $"{model.SpeedMBps:0.0} MB/s" : "-- MB/s";
        TimeFormatted = model.Timestamp.ToLocalTime().ToString("HH:mm");
        DateFormatted = model.Timestamp.ToLocalTime().ToString("MMM dd, yyyy");
    }

    public TransferRecord Model { get; set; }
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? FilePath { get; set; }
    public long Bytes { get; set; }
    public string SizeFormatted { get; set; } = "";
    public string Direction { get; set; } = "";
    public string DirectionLabel { get; set; } = "";
    public string DirectionBadge { get; set; } = "";
    public string DirectionTone { get; set; } = "accent";
    public string DirectionIcon { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DeviceIp { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public double SpeedMBps { get; set; }
    public string SpeedFormatted { get; set; } = "";
    public string Status { get; set; } = "";
    public string TimeFormatted { get; set; } = "";
    public string DateFormatted { get; set; } = "";

    [RelayCommand]
    private void OpenFile()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
        {
            _parent.NotifyUser("File not found on disk: " + FileName, "warn");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _parent.NotifyUser("Cannot open file: " + ex.Message, "error");
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            _parent.NotifyUser("No file path recorded.", "warn");
            return;
        }

        try
        {
            if (File.Exists(FilePath))
            {
                Process.Start("explorer.exe", "/select,\"" + FilePath + "\"");
            }
            else
            {
                string? dir = Path.GetDirectoryName(FilePath);
                if (Directory.Exists(dir))
                {
                    Process.Start("explorer.exe", "\"" + dir + "\"");
                }
                else
                {
                    _parent.NotifyUser("Folder does not exist on disk.", "warn");
                }
            }
        }
        catch (Exception ex)
        {
            _parent.NotifyUser("Cannot open folder: " + ex.Message, "error");
        }
    }
}

public sealed partial class PairedDeviceItem : ObservableObject
{
    private readonly PairedDevice _device;
    private readonly MobileHubViewModel _parent;

    public PairedDeviceItem(PairedDevice device, MobileHubViewModel parent)
    {
        _device = device;
        _parent = parent;
        Id = device.Id;
        Name = device.Name;
        IpAddress = device.IpAddress;
        PairedAtFormatted = device.PairedAt.ToString("g");
        LastSeenFormatted = device.LastSeen.ToString("t");
        IsBlocked = device.IsBlocked;
        KeyText = device.TokenHash == null ? "old shared key" : "own key";
    }

    public string KeyText { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string PairedAtFormatted { get; set; } = string.Empty;
    public string LastSeenFormatted { get; set; } = string.Empty;
    public bool IsBlocked { get; set; }

    [RelayCommand]
    private void Disconnect() => _parent.RemoveDevice(this);
}
