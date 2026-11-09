using System.Windows;
using CloudPlayX.App;
using CloudPlayX.Core;
using CloudPlayX.Diagnostics;
using CloudPlayX.Games;
using Microsoft.Win32;

namespace CloudPlayX.Desktop.Views;

public partial class GameSettingsWindow : Window
{
    private readonly AppHost _host;
    private readonly GameEntry _game;

    public GameSettingsWindow(AppHost host, GameEntry game)
    {
        _host = host;
        _game = game;
        InitializeComponent();
        TitleText.Text = game.Name;
        NameBox.Text = game.Name;
        ArgsBox.Text = game.Arguments;
        WorkBox.Text = game.WorkingDirectory ?? "";
        var candidates = new List<string>();
        if (host.Mounts.Get(game.MountId) is { } m)
        {
            var root = Path.Combine(m.Root, game.RootPath.Replace('/', '\\'));
            try
            {
                candidates.AddRange(Directory.EnumerateFiles(root, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true })
                    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Take(200));
                ExeHint.Text = $"{candidates.Count} executable(s) found. The scanner's choice was: {game.Compatibility?.Executable ?? "none"}.";
            }
            catch (IOException) { }
        }
        else ExeHint.Text = "Mount the drive to list executables.";
        if (game.Executable != null && !candidates.Contains(game.Executable)) candidates.Insert(0, game.Executable);
        ExeBox.ItemsSource = candidates;
        ExeBox.SelectedItem = game.Executable;
        ModeCloud.IsChecked = game.Mode == ExecutionMode.CloudDirect;
        ModeHybrid.IsChecked = game.Mode == ExecutionMode.HybridOptimized;
        ModeLocal.IsChecked = game.Mode == ExecutionMode.FullLocal;
        var canLink = DependencyChecker.CanCreateSymlinks();
        ModeHybrid.IsEnabled = canLink;
        ModeHint.Text = (canLink ? "" : "Hybrid needs Windows Developer Mode (Settings → System → For developers) or administrator rights for symbolic links. ")
                        + $"Recommended by analysis: {game.Compatibility?.RecommendedMode.ToString() ?? "unknown"}.";

        // Save games setup
        CloudSaveBox.IsChecked = game.CloudSaveSyncEnabled;
        SavePathBox.Text = game.CustomSavePath ?? _host.SaveBackups.DetectSavePath(game) ?? "";
        UpdateSaveHint();

        AwareBox.IsChecked = game.GameAwareEnabled;
        AwareHint.Text = $"Requires a mod/plugin that writes JSON lines to \\\\.\\pipe\\cloudplayx-telemetry-{new string(game.Id.Where(char.IsLetterOrDigit).ToArray())} (current Windows user only). " +
                         "No game ships such a plugin today; without one this option has no effect. See docs/GAME_AWARE_PROTOCOL.md.";
    }

    private void UpdateSaveHint()
    {
        var detected = _host.SaveBackups.DetectSavePath(_game);
        if (string.IsNullOrWhiteSpace(SavePathBox.Text))
        {
            SavePathHint.Text = detected != null ? $"Auto-detected: {detected}" : "No save folder detected automatically. Click Browse to select your save folder.";
        }
        else if (Directory.Exists(SavePathBox.Text))
        {
            SavePathHint.Text = "Save folder verified.";
        }
        else
        {
            SavePathHint.Text = "Folder does not exist yet; will be created on save.";
        }

        if (_game.LastSaveBackup.HasValue)
        {
            SaveStatusText.Text = $"Last cloud backup: {_game.LastSaveBackup:g} ({_game.SaveBackupCount} snapshot(s) stored)";
        }
    }

    private void AutoDetectSave(object sender, RoutedEventArgs e)
    {
        var detected = _host.SaveBackups.DetectSavePath(_game);
        if (detected != null)
        {
            SavePathBox.Text = detected;
            UpdateSaveHint();
            SaveStatusText.Text = "Save directory detected automatically!";
        }
        else
        {
            MessageBox.Show(this, "Could not automatically locate the save folder for this game. Please click 'Browse…' to select it manually.", "Save Location", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BrowseSavePath(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = $"Select Save Game Folder for {_game.Name}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dlg.ShowDialog(this) == true)
        {
            SavePathBox.Text = dlg.FolderName;
            UpdateSaveHint();
        }
    }

    private async void BackupSavesNow(object sender, RoutedEventArgs e)
    {
        BackupNowBtn.IsEnabled = false;
        SaveStatusText.Text = "Backing up save files to cloud…";
        try
        {
            _game.CustomSavePath = string.IsNullOrWhiteSpace(SavePathBox.Text) ? null : SavePathBox.Text.Trim();
            var mount = _host.Mounts.Get(_game.MountId);
            var remote = mount?.Definition.RemoteName ?? _host.Remotes.FirstOrDefault()?.Name;
            var res = await _host.SaveBackups.BackupAsync(_game, _host.Rclone, _host.RcloneConfig, remote);
            if (res.Success)
            {
                SaveStatusText.Text = res.Message;
                _host.Library.Save(_game);
            }
            else
            {
                SaveStatusText.Text = "Backup skipped: " + res.Message;
            }
        }
        catch (Exception ex)
        {
            SaveStatusText.Text = "Backup error: " + ex.Message;
        }
        finally
        {
            BackupNowBtn.IsEnabled = true;
        }
    }

    private async void RestoreSavesNow(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Restore latest save files from cloud for '{_game.Name}'?\n\nA safety snapshot of your current local save files will be saved before restoring.",
            "Confirm Restore", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        RestoreBtn.IsEnabled = false;
        SaveStatusText.Text = "Restoring save files from cloud…";
        try
        {
            _game.CustomSavePath = string.IsNullOrWhiteSpace(SavePathBox.Text) ? null : SavePathBox.Text.Trim();
            var mount = _host.Mounts.Get(_game.MountId);
            var remote = mount?.Definition.RemoteName ?? _host.Remotes.FirstOrDefault()?.Name;
            var res = await _host.SaveBackups.RestoreAsync(_game, _host.Rclone, _host.RcloneConfig, remote);
            if (res.Success)
            {
                SaveStatusText.Text = res.Message;
            }
            else
            {
                SaveStatusText.Text = "Restore failed: " + res.Message;
            }
        }
        catch (Exception ex)
        {
            SaveStatusText.Text = "Restore error: " + ex.Message;
        }
        finally
        {
            RestoreBtn.IsEnabled = true;
        }
    }

    private void Ok(object sender, RoutedEventArgs e)
    {
        _game.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _game.Name : NameBox.Text.Trim();
        _game.Executable = ExeBox.SelectedItem as string ?? _game.Executable;
        _game.Arguments = ArgsBox.Text;
        _game.WorkingDirectory = string.IsNullOrWhiteSpace(WorkBox.Text) ? null : WorkBox.Text.Trim().Replace('\\', '/');
        _game.Mode = ModeHybrid.IsChecked == true ? ExecutionMode.HybridOptimized : ModeLocal.IsChecked == true ? ExecutionMode.FullLocal : ExecutionMode.CloudDirect;
        _game.GameAwareEnabled = AwareBox.IsChecked == true;
        _game.CloudSaveSyncEnabled = CloudSaveBox.IsChecked == true;
        _game.CustomSavePath = string.IsNullOrWhiteSpace(SavePathBox.Text) ? null : SavePathBox.Text.Trim();
        _host.Library.Save(_game);
        DialogResult = true;
    }
}
