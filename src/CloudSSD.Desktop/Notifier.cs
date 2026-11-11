using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CloudSSD.Desktop;

/// <summary>
/// A small note in the corner of the screen that says what an auto mode just did, so the user need not open a page
/// to know it worked. It never takes the keyboard, goes away by itself and closes on a click. One at a time: a new
/// note replaces the one still showing.
/// </summary>
public static class Notifier
{
    private static readonly TimeSpan Stay = TimeSpan.FromSeconds(8);
    private static Window? _current;

    /// <summary>Safe to call from any thread; does nothing when the app is shutting down.</summary>
    public static void Show(string title, string text)
    {
        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(() =>
        {
            _current?.Close();
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = Brush("Accent") });
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), Foreground = Brush("Text") });
            var note = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent,
                ShowInTaskbar = false, Topmost = true, ShowActivated = false, Focusable = false, Width = 380, SizeToContent = SizeToContent.Height,
                Title = "Cloud-SSD note",
                Content = new Border { Background = Brush("Surface2"), BorderBrush = Brush("Accent"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Child = panel },
            };
            var timer = new DispatcherTimer { Interval = Stay };
            timer.Tick += (_, _) => note.Close();
            note.Loaded += (_, _) =>
            {
                var area = SystemParameters.WorkArea;
                note.Left = area.Right - note.ActualWidth - 16;
                note.Top = area.Bottom - note.ActualHeight - 16;
            };
            note.MouseLeftButtonUp += (_, _) => note.Close();
            note.Closed += (_, _) =>
            {
                timer.Stop();
                if (ReferenceEquals(_current, note)) _current = null;
            };
            _current = note;
            note.Show();
            timer.Start();
        });
    }

    private static Brush Brush(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}
