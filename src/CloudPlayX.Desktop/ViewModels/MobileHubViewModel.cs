using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CloudPlayX.App;
using CloudPlayX.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudPlayX.Desktop.ViewModels;

public sealed partial class MobileHubViewModel : PageViewModel
{
    public MobileHubViewModel(AppHost host) : base(host, "Mobile Hub", "\uE8EA")
    {
        UploadDirectory = host.Settings.MobileBridge.UploadDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Cloud-SSD_Uploads");

        PhotosBackupDirectory = host.Settings.MobileBridge.PhotoBackupDirectory
            ?? Path.Combine(host.Paths.DataRoot, "Mobile_Backups", "Photos");

        _allowDriveBrowsing = host.Settings.MobileBridge.AllowDriveBrowsing;
        _allowStreaming = host.Settings.MobileBridge.AllowStreaming;
        _allowUploads = host.Settings.MobileBridge.AllowUploads;
        _allowGamingHud = host.Settings.MobileBridge.AllowGamingHud;

        Host.MobileBridge.StatusChanged += () => Ui(RefreshState);
        Host.MobileBridge.FileTransferred += (name, bytes) => Ui(() =>
        {
            RecentTransfers.Insert(0, $"• {name} ({ByteSize.Format(bytes)}) at {DateTime.Now:HH:mm:ss}");
            if (RecentTransfers.Count > 10) RecentTransfers.RemoveAt(RecentTransfers.Count - 1);
        });

        RefreshState();
    }

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "Bridge Stopped";
    [ObservableProperty] private string _statusTone = "warn";
    [ObservableProperty] private string _localIp = "127.0.0.1";
    [ObservableProperty] private int _port = 8090;
    [ObservableProperty] private string _localUrl = "http://127.0.0.1:8090/";
    [ObservableProperty] private string _pairingUrl = "";
    [ObservableProperty] private string _pairingToken = "";
    [ObservableProperty] private BitmapSource? _qrCode;
    [ObservableProperty] private string _transferRateFormatted = "0.0 MB/s";
    [ObservableProperty] private string _uploadDirectory;
    [ObservableProperty] private string _photosBackupDirectory;
    [ObservableProperty] private bool _allowDriveBrowsing;
    [ObservableProperty] private bool _allowStreaming;
    [ObservableProperty] private bool _allowUploads;
    [ObservableProperty] private bool _allowGamingHud;

    public ObservableCollection<PairedDeviceItem> PairedDevices { get; } = new();
    public ObservableCollection<string> RecentTransfers { get; } = new();

    public override void OnShown()
    {
        base.OnShown();
        RefreshState();
    }

    public override void OnMetrics(EngineMetrics m)
    {
        base.OnMetrics(m);
        double rateMbps = Host.MobileBridge.CurrentTransferRateMbps;
        TransferRateFormatted = rateMbps > 0 ? $"{rateMbps / 8.0:0.0} MB/s ({rateMbps:0.0} Mbps)" : "0.0 MB/s";
    }

    private void RefreshState()
    {
        IsRunning = Host.MobileBridge.IsRunning;
        StatusText = IsRunning ? "Local Bridge Running" : "Local Bridge Stopped";
        StatusTone = IsRunning ? "ok" : "warn";
        LocalIp = Host.MobileBridge.LocalIpAddress;
        Port = Host.MobileBridge.Port;
        LocalUrl = Host.MobileBridge.LocalUrl;
        PairingToken = Host.MobileBridge.PairingToken;
        PairingUrl = $"{LocalUrl}?token={PairingToken}";

        try
        {
            QrCode = QrCodeGenerator.GenerateQrBitmap(PairingUrl, 8);
        }
        catch
        {
            QrCode = null;
        }

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
        Host.Settings.MobileBridge.PairingToken = Guid.NewGuid().ToString("N")[..12];
        Host.SaveSettings();
        RefreshState();
        Say("New pairing token generated. Existing phones must rescan QR code.", "ok");
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
        if (!Confirm("Are you sure you want to disconnect all paired mobile devices?", "Cloud-SSD Mobile Hub")) return;
        Host.Settings.MobileBridge.PairedDevices.Clear();
        Host.SaveSettings();
        RefreshState();
        Say("All paired mobile devices have been disconnected.", "ok");
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
        Host.Settings.MobileBridge.PairedDevices.RemoveAll(d => d.Id == item.Id);
        Host.SaveSettings();
        RefreshState();
        Say($"Device '{item.Name}' removed.", "ok");
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
    }

    public string Id { get; }
    public string Name { get; }
    public string IpAddress { get; }
    public string PairedAtFormatted { get; }
    public string LastSeenFormatted { get; }
    public bool IsBlocked { get; }

    [RelayCommand]
    private void Disconnect() => _parent.RemoveDevice(this);
}
