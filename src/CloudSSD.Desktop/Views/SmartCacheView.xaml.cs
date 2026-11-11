using System.Windows.Controls;

namespace CloudSSD.Desktop.Views;

public partial class SmartCacheView : UserControl
{
    public SmartCacheView()
    {
        InitializeComponent();
        // The window translates before this tab's view exists, so the view translates itself.
        Loaded += (_, _) => LocalizationManager.TranslateTree(this);
    }
}
