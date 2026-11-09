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

        Host.MobileBridge.StatusChanged += () => Ui(RefreshState);
        Host.MobileBridge.FileTransferred += (name, bytes) => Ui(() =>
        {
            RecentTransfers.Insert(0, $"• {name} ({ByteSize.Format(bytes)}) at {DateTime.Now:HH:mm:ss}");
            if (RecentTransfers.Count > 10) RecentTransfers.RemoveAt(RecentTransfers.Count - 1);
        });
        Host.MobileBridge.TransferRecorded += record => Ui(() => OnTransferRecorded(record));

        RefreshState();
        LoadTransferHistory();
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

    [ObservableProperty] private string _selectedFilter = "All";
    [ObservableProperty] private string _transferSearchText = "";
    [ObservableProperty] private int _totalTransferCount;


    public ObservableCollection<PairedDeviceItem> PairedDevices { get; } = new();
    public ObservableCollection<string> RecentTransfers { get; } = new();
    public ObservableCollection<TransferRecordViewModel> AllTransfers { get; } = new();
    public ObservableCollection<TransferRecordViewModel> FilteredTransfers { get; } = new();

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
        if (!Confirm("Are you sure you want to disconnect all paired mobile devices?", "Cloud Drive Mobile Hub")) return;
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
    }

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string PairedAtFormatted { get; set; } = string.Empty;
    public string LastSeenFormatted { get; set; } = string.Empty;
    public bool IsBlocked { get; set; }

    [RelayCommand]
    private void Disconnect() => _parent.RemoveDevice(this);
}
