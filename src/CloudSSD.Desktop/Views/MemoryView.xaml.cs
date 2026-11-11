using System.Windows.Controls;

namespace CloudSSD.Desktop.Views;

public partial class MemoryView : UserControl
{
    public MemoryView()
    {
        InitializeComponent();
        // The window translates its tree before a newly selected page exists, so this page translates itself.
        Loaded += (_, _) => LocalizationManager.TranslateTree(this);
    }
}
