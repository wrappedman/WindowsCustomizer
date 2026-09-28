using System.Runtime.InteropServices;
using System.Windows;
using WindowsCustomizer.App.ViewModels;

namespace WindowsCustomizer.App.Views;

/// <summary>
/// Shell window: sidebar navigation + content host + status/log bar.
/// UI never talks to DISM or the file system directly — everything goes through MainViewModel.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TryLoadWindowIcon();
    }

    /// <summary>
    /// Loads the window/taskbar icon from the embedded resource — fully guarded so a missing
    /// or undecodable icon can NEVER crash the app (the EXE icon comes from ApplicationIcon).
    /// </summary>
    private void TryLoadWindowIcon()
    {
        foreach (var uri in new[] { "app.ico", "/WindowsCustomizer;component/app.ico", "pack://application:,,,/app.ico" })
        {
            try
            {
                var streamInfo = Application.GetResourceStream(new Uri(uri, UriKind.RelativeOrAbsolute));
                if (streamInfo?.Stream is null)
                {
                    continue;
                }

                using var stream = streamInfo.Stream;
                var frame = System.Windows.Media.Imaging.BitmapFrame.Create(
                    stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                frame.Freeze();
                Icon = frame;
                return;
            }
            catch
            {
                // try the next strategy; worst case the window keeps the default icon
            }
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { DataContext: NavItemViewModel nav } &&
            DataContext is MainViewModel vm)
        {
            vm.NavigateTo(nav);
        }
    }
}
