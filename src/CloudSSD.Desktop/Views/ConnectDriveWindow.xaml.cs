using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CloudSSD.App;
using CloudSSD.Diagnostics;
using CloudSSD.Mount;
using Microsoft.Win32;

namespace CloudSSD.Desktop.Views;

public partial class ConnectDriveWindow : Window
{
    private readonly AppHost _host;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private GoogleClientFile? _key;
    private readonly GoogleClientFile? _bundled = GoogleClientFile.FindBundled();
    private string? _fixUrl;

    public ConnectDriveWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        SetKey(GoogleClientFile.FindDownloaded());
    }

    public string RemoteName { get; private set; } = "";
    public string ProviderType { get; private set; } = "drive";
    /// <summary>True when the login only has read permission; the drive must then be mounted read-only.</summary>
    public bool ReadOnlyScope { get; private set; }

    private void OpenFix(object s, RoutedEventArgs e) { if (_fixUrl != null) Open(_fixUrl); }

    private string NextRemoteName(string prefix)
    {
        var name = prefix;
        for (int i = 2; _host.Remotes.Any(r => r.Name == name); i++) name = prefix + i;
        return name;
    }

    private void OnProviderChanged(object sender, RoutedEventArgs e)
    {
        if (GooglePanel == null || OneDrivePanel == null || WebDavPanel == null) return;
        GooglePanel.Visibility = TabGoogle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        OneDrivePanel.Visibility = TabOneDrive.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WebDavPanel.Visibility = TabWebDav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Status.Text = "";
    }

    private void OneDriveAdvancedChanged(object sender, RoutedEventArgs e)
    {
        OneDriveAdvancedPanel.Visibility = OneDriveAdvancedBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetKey(GoogleClientFile? key)
    {
        _key = key;
        KeyFoundBox.Visibility = key is null ? Visibility.Collapsed : Visibility.Visible;
        GetKeyBox.Visibility = key is null ? Visibility.Visible : Visibility.Collapsed;
        TesterButton.Visibility = key?.TestUsersUrl is null ? Visibility.Collapsed : Visibility.Visible;
        if (key != null)
        {
            KeyFileText.Text = $"{Path.GetFileName(key.Path)}{(key.ProjectId != null ? $" · project {key.ProjectId}" : "")}. " +
                               "First time? Add your Gmail as a tester or Google will say “Access blocked”.";
            ModeText.Text = "Uses your own Google key from the file above.";
        }
        else if (_bundled != null)
        {
            ModeText.Text = "Your browser opens Google's sign-in page. Choose your Gmail and press Allow.";
        }
        else
        {
            ModeText.Text = "Your browser opens Google's sign-in page. Choose your Gmail and press Allow. " +
                            "Google's page shows the name “rclone” — that is the engine Cloud-SSD uses, it is expected.";
        }
    }

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void OpenConsole(object s, RoutedEventArgs e) => Open("https://console.cloud.google.com/auth/clients");
    private void OpenTesters(object s, RoutedEventArgs e) { if (_key?.TestUsersUrl is { } u) Open(u); }

    private void FindKey(object s, RoutedEventArgs e)
    {
        var key = GoogleClientFile.FindDownloaded();
        if (key is null)
        {
            ShowError("No Google key file (client_secret_….json) found in Downloads or on the Desktop yet. In Google Cloud open your Desktop client and click Download JSON.");
            return;
        }
        Status.Text = "";
        SetKey(key);
    }

    private void LoadKey(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose the Google key file you downloaded",
            Filter = "Google OAuth client (*.json)|*.json",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };
        if (dlg.ShowDialog(this) != true) return;
        var key = GoogleClientFile.TryLoad(dlg.FileName);
        if (key is null)
        {
            ShowError("That file is not a Google OAuth client file (it needs a client_id and client_secret).");
            return;
        }
        Status.Text = "";
        SetKey(key);
    }

    private void AdvancedChanged(object sender, RoutedEventArgs e) =>
        AdvancedPanel.Visibility = AdvancedBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private async void QuickConnect(object sender, RoutedEventArgs e)
    {
        var key = _key ?? _bundled;
        await RunAsync(new GoogleDriveConnector.Request(NextRemoteName("gdrive"), key?.ClientId ?? "", key?.ClientSecret ?? "", ReadOnlyBox.IsChecked == true, null));
    }

    private async void Connect(object sender, RoutedEventArgs e) =>
        await RunAsync(new GoogleDriveConnector.Request(NextRemoteName("gdrive"), ClientIdBox.Text.Trim(), SecretBox.Password, ReadOnlyBox.IsChecked == true,
            string.IsNullOrWhiteSpace(RootBox.Text) ? null : RootBox.Text.Trim()));

    private async void ConnectOneDrive(object sender, RoutedEventArgs e)
    {
        var isBusiness = OneDriveBusinessRadio.IsChecked == true;
        var req = new OneDriveConnector.Request(
            NextRemoteName("onedrive"),
            isBusiness ? "business" : "personal",
            string.IsNullOrWhiteSpace(OneDriveClientIdBox.Text) ? null : OneDriveClientIdBox.Text.Trim(),
            string.IsNullOrWhiteSpace(OneDriveSecretBox.Password) ? null : OneDriveSecretBox.Password,
            ReadOnlyBox.IsChecked == true);

        await RunActionAsync("onedrive", req.RemoteName, req.ReadOnlyScope, async (progress, ct) =>
        {
            await _host.OneDrive.ConnectAsync(_host.Rclone!, req, progress, ct);
        });
    }

    private async void ConnectWebDav(object sender, RoutedEventArgs e)
    {
        var url = WebDavUrlBox.Text.Trim();
        var vendor = (WebDavVendorBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "other";
        var user = WebDavUserBox.Text.Trim();
        var pass = WebDavPassBox.Password;

        if (string.IsNullOrWhiteSpace(url) || url == "https://")
        {
            ShowError("Please enter your WebDAV server URL.");
            return;
        }
        if (string.IsNullOrWhiteSpace(user))
        {
            ShowError("Please enter your username.");
            return;
        }

        var req = new WebDavConnector.Request(
            NextRemoteName("webdav"),
            url,
            vendor,
            user,
            pass,
            ReadOnlyBox.IsChecked == true);

        await RunActionAsync("webdav", req.RemoteName, req.ReadOnlyScope, async (progress, ct) =>
        {
            await _host.WebDav.ConnectAsync(_host.Rclone!, req, progress, ct);
        });
    }

    private async Task RunActionAsync(string provider, string remoteName, bool readOnly, Func<IProgress<string>, CancellationToken, Task> action)
    {
        if (_busy) return;
        if (_host.Rclone is null)
        {
            ShowError("rclone was not found; see Diagnostics.");
            return;
        }
        _busy = true;
        _cts = new CancellationTokenSource();
        Status.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
        FixButton.Visibility = Visibility.Collapsed;
        SetButtonsEnabled(false);
        Progress.Visibility = Visibility.Visible;
        var progress = new Progress<string>(m => Status.Text = m);

        try
        {
            await action(progress, _cts.Token);
            if (_host.Settings.EncryptRcloneConfig)
            {
                Status.Text = "Encrypting the saved login (Windows DPAPI)…";
                await _host.RcloneConfig.EnsureEncryptedAsync(_host.Rclone, _host.CpxPath, _cts.Token);
            }
            RemoteName = remoteName;
            ProviderType = provider;
            ReadOnlyScope = readOnly;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            Status.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            ShowError(Redactor.Redact(ex.Message));
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private async Task RunAsync(GoogleDriveConnector.Request req)
    {
        if (_busy) return;
        if (_host.Rclone is null)
        {
            ShowError("rclone was not found; see Diagnostics.");
            return;
        }
        _busy = true;
        _cts = new CancellationTokenSource();
        Status.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
        FixButton.Visibility = Visibility.Collapsed;
        SetButtonsEnabled(false);
        Progress.Visibility = Visibility.Visible;
        var ownKey = !req.UsesSharedClient && req.ClientId != _bundled?.ClientId;
        var progress = new Progress<string>(m => Status.Text = m + (!ownKey ? "" :
            "  If Google shows “Access blocked”, click “Add me as a tester”, add your Gmail, then press Cancel and sign in again."));
        try
        {
            await _host.Drive.ConnectAsync(_host.Rclone, req, progress, _cts.Token);
            if (_host.Settings.EncryptRcloneConfig)
            {
                Status.Text = "Encrypting the saved login (Windows DPAPI)…";
                await _host.RcloneConfig.EnsureEncryptedAsync(_host.Rclone, _host.CpxPath, _cts.Token);
            }
            RemoteName = req.RemoteName;
            ProviderType = "drive";
            ReadOnlyScope = req.ReadOnlyScope;
            SecretBox.Clear();
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            Status.Text = "Cancelled.";
        }
        catch (GoogleSetupException setup)
        {
            Status.Foreground = (System.Windows.Media.Brush)FindResource("Warning");
            Status.Text = setup.Message;
            _fixUrl = setup.FixUrl;
            FixButton.Content = setup.FixLabel;
            FixButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            var msg = Redactor.Redact(ex.Message);
            if (req.UsesSharedClient && (msg.Contains("client", StringComparison.OrdinalIgnoreCase) || msg.Contains("403") || msg.Contains("401")))
                msg += "  Google may no longer accept rclone's shared login — use your own Google key (box above).";
            ShowError(msg);
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        QuickButton.IsEnabled = ConnectButton.IsEnabled = OneDriveQuickButton.IsEnabled = WebDavConnectButton.IsEnabled = enabled;
    }

    private void ShowError(string text)
    {
        Status.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
        Status.Text = text;
    }

    private void Cancel(object sender, RoutedEventArgs e)
    {
        if (_busy && _cts is { IsCancellationRequested: false }) _cts.Cancel();
        else DialogResult = false;
    }
}
