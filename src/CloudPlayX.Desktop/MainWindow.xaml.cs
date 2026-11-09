using System.Windows;
using System.Windows.Controls;

namespace CloudPlayX.Desktop;

public partial class MainWindow : Window
{
    private bool _initializing = true;

    public MainWindow()
    {
        InitializeComponent();

        CmbLanguage.ItemsSource = LocalizationManager.SupportedLanguages;
        CmbLanguage.SelectedItem = LocalizationManager.CurrentLanguage;
        _initializing = false;

        Loaded += (_, _) => LocalizationManager.TranslateTree(this);
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (CmbLanguage.SelectedItem is string lang)
        {
            LocalizationManager.SetLanguage(lang);
        }
    }
}
