using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CloudSSD.Desktop.Views;

public partial class SpaceView : UserControl
{
    public SpaceView()
    {
        InitializeComponent();
        Loaded += (_, _) => LocalizationManager.TranslateTree(this);
    }

    /// <summary>The ⋮ button opens its menu with a normal click (a context menu would otherwise need the right button).</summary>
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.DataContext = button.DataContext;
        menu.IsOpen = true;
    }
}
