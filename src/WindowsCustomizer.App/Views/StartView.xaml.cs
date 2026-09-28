using System.Windows;
using System.Windows.Input;
using WindowsCustomizer.App.ViewModels;
using WindowsCustomizer.Core.Services;

namespace WindowsCustomizer.App.Views;

public partial class StartView : System.Windows.Controls.UserControl
{
    public StartView()
    {
        InitializeComponent();
    }

    private void NewProject_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is StartViewModel vm)
        {
            vm.NewProjectCommand.Execute(null);
        }
    }

    private void OpenProject_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is StartViewModel vm)
        {
            vm.OpenProjectCommand.Execute(null);
        }
    }

    private void Recent_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is StartViewModel vm &&
            (sender as FrameworkElement)?.DataContext is RecentProjectItem item)
        {
            vm.OpenRecentCommand.Execute(item);
        }
    }
}
