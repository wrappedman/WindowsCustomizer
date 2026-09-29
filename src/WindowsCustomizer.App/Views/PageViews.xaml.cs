using System.Windows.Controls;

namespace WindowsCustomizer.App.Views;

// Combined code-behind for the customization/build pages. Each XAML file declares its x:Class;
// the generated partial provides InitializeComponent.

public partial class UsersView : UserControl
{
    public UsersView() => InitializeComponent();
}

public partial class AppsView : UserControl
{
    public AppsView() => InitializeComponent();
}

public partial class DriversView : UserControl
{
    public DriversView() => InitializeComponent();
}

public partial class UpdatesView : UserControl
{
    public UpdatesView() => InitializeComponent();
}

public partial class RegistryView : UserControl
{
    public RegistryView() => InitializeComponent();
}

public partial class ServicesView : UserControl
{
    public ServicesView() => InitializeComponent();

    private void ServiceStartup_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Only react to real user interaction — initial binding/row creation also raises this
        // event and must NOT trigger confirmations or saves (that caused a change->refresh->change
        // recursion).
        if (sender is not System.Windows.Controls.ComboBox combo ||
            !combo.IsDropDownOpen && !combo.IsKeyboardFocusWithin && !combo.IsMouseCaptureWithin)
        {
            return;
        }

        if (combo.DataContext is WindowsCustomizer.Core.Models.ServiceEntry entry &&
            DataContext is ViewModels.ServicesViewModel viewModel)
        {
            viewModel.SetStartupCommand.Execute(entry);
        }
    }
}

public partial class FeaturesView : UserControl
{
    public FeaturesView() => InitializeComponent();
}

public partial class ScriptsView : UserControl
{
    public ScriptsView() => InitializeComponent();
}

public partial class BrandingView : UserControl
{
    public BrandingView() => InitializeComponent();
}

public partial class ReviewView : UserControl
{
    public ReviewView() => InitializeComponent();
}

public partial class BuildView : UserControl
{
    public BuildView() => InitializeComponent();
}

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        Loaded += (_, _) => TryLoadHeroIcon();
    }

    /// <summary>Loads the app icon into the hero — guarded; on failure the MDL2 glyph stays visible.</summary>
    private void TryLoadHeroIcon()
    {
        foreach (var uri in new[] { "app-256.png", "/WindowsCustomizer;component/app-256.png", "pack://application:,,,/app-256.png" })
        {
            try
            {
                var streamInfo = System.Windows.Application.GetResourceStream(new Uri(uri, UriKind.RelativeOrAbsolute));
                if (streamInfo?.Stream is null)
                {
                    continue;
                }

                using var stream = streamInfo.Stream;
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                HeroIcon.Source = image;
                return;
            }
            catch
            {
                // keep the glyph fallback
            }
        }
    }
}
