using System.Windows;
using System.Windows.Threading;
using WindowsCustomizer.App.Infrastructure;
using WindowsCustomizer.App.ViewModels;
using WindowsCustomizer.App.Views;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Services;
using WindowsCustomizer.Core.Commands;
using WindowsCustomizer.Core.Engines;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.App;

/// <summary>Composition root: wires engines, services, logging and the main window (MVVM).</summary>
public partial class App : Application
{
    private FileLogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appPaths = new AppPaths();
        appPaths.EnsureCreated();

        var settingsStore = new AppSettingsStore(appPaths);
        var settings = settingsStore.Load();

        _logger = new FileLogger(appPaths.LogsDirectory);
        _logger.Info("App", $"Windows Customizer Studio starting (version {AppInfo.Version}).");
        _logger.Info("App", $"Data root: {appPaths.Root}");

        if (PrivilegeService.IsAdministrator)
        {
            _logger.Info("App", "Running with administrator privileges.");
        }
        else
        {
            _logger.Info("App", "Running without elevation (administrator required for servicing operations).");
        }

        var dialogService = new DialogService();
        var processRunner = new ProcessRunner(_logger);
        var projectService = new ProjectService(_logger);
        var recentService = new RecentProjectsService(appPaths, _logger);
        var isoAnalyzer = new IsoAnalyzer(_logger);
        var dismEngine = new DismEngine(processRunner, _logger);
        var registryEngine = new RegistryEngine(processRunner, _logger);
        var isoBuilder = new IsoBuilder(processRunner, _logger);
        var presetLibrary = new PresetLibrary(appPaths, _logger);
        var snapshotService = new ProjectSnapshotService(_logger);
        var validationEngine = new ValidationEngine(_logger);
        var dependencyEngine = new DependencyEngine();
        var buildPipeline = new BuildPipeline(dismEngine, registryEngine, isoBuilder, processRunner, validationEngine, _logger);

        var mainViewModel = new MainViewModel(
            appPaths,
            settingsStore,
            settings,
            _logger,
            projectService,
            recentService,
            isoAnalyzer,
            dismEngine,
            registryEngine,
            isoBuilder,
            presetLibrary,
            snapshotService,
            validationEngine,
            dependencyEngine,
            buildPipeline,
            dialogService);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var window = new MainWindow
        {
            DataContext = mainViewModel
        };
        MainWindow = window;
        window.Show();

        mainViewModel.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("App", "Application exiting.");
        _logger?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Spec: the app must never crash silently — log, explain, keep running.
    /// Re-entrancy guard: a dialog shown while another is open can re-trigger layout retries and
    /// grow the stack (0xc00000fd). Only the first error shows a dialog; the rest are just logged.
    /// </summary>
    private static int _errorDialogOpen;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            _logger?.Error("App", "Unhandled UI exception.", e.Exception);

            if (Interlocked.CompareExchange(ref _errorDialogOpen, 1, 0) == 0)
            {
                try
                {
                    MessageBox.Show(
                        $"An unexpected error occurred.\n\nWhat happened: {e.Exception.Message}\n\n" +
                        "The error was written to the application log (Logs section).\n" +
                        "Please retry the operation; if it keeps failing, attach the log file to a bug report.",
                        "Windows Customizer Studio",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    Interlocked.Exchange(ref _errorDialogOpen, 0);
                }
            }
        }
        catch
        {
            // Last-resort: never let error reporting itself crash the app.
        }

        e.Handled = true;
    }
}

/// <summary>Assembly metadata helpers.</summary>
public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public const string ProductName = "Windows Customizer Studio";
}
