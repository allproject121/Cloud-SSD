using System.Windows;
using System.Windows.Controls;
using CloudSSD.Desktop.ViewModels;

namespace CloudSSD.Desktop.Views;

public partial class MobileHubView : UserControl
{
    public MobileHubView()
    {
        InitializeComponent();
    }

    private void OnDropZoneDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0 && DataContext is MobileHubViewModel vm)
            {
                foreach (var f in files)
                {
                    vm.RecentTransfers.Insert(0, $"• Prepared for phone: {System.IO.Path.GetFileName(f)}");
                }
            }
        }
    }
}
