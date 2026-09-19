using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OpenDMXBridge.Services;
using OpenDMXBridge.Services.ArtNet;
using OpenDMXBridge.Services.Contracts;
using OpenDMXBridge.Services.Dmx;
using OpenDMXBridge.Services.Outputs;
using OpenDMXBridge.ViewModels;

namespace OpenDMXBridge;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        Services = _serviceProvider;

        var logger = Services.GetRequiredService<ILoggingService>();

        DispatcherUnhandledException += (_, args) =>
        {
            logger.Error($"Erreur UI : {args.Exception.Message}", nameof(App));
            WriteCrashLog("UI", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                logger.Error($"Erreur fatale : {ex.Message}", nameof(App));
                WriteCrashLog("Fatal", ex);
            }
        };

        var settings = Services.GetRequiredService<ISettingsService>();
        settings.Load();

        var mainWindow = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainViewModel>()
        };
        mainWindow.Show();
    }

    /// <summary>Trace les exceptions non gérées dans %LOCALAPPDATA%\OpenDMXBridge\crash.log.</summary>
    private static void WriteCrashLog(string origin, Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenDMXBridge");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{origin}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // ne jamais échouer dans le gestionnaire d'erreurs
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IFtdiDriverStatus, FtdiDriverStatus>();

        services.AddSingleton<OpenDmxOutput>();
        services.AddSingleton<NullDmxOutput>();
        services.AddSingleton<EnttecProOutput>();
        services.AddSingleton<DmxKingOutput>();
        services.AddSingleton<SacnOutput>();
        services.AddSingleton<ArtNetOutput>();
        services.AddSingleton<IDmxOutputFactory, DmxOutputFactory>();

        services.AddSingleton<IDmxEngine, DmxEngine>();
        services.AddSingleton<INetworkService, ArtNetNetworkService>();
        services.AddSingleton<IBridgeOrchestrator, BridgeOrchestrator>();
        services.AddSingleton<ConsoleViewModel>();
        services.AddSingleton<MainViewModel>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
        {
            var bridge = _serviceProvider.GetService<IBridgeOrchestrator>();
            if (bridge is IAsyncDisposable disposable)
                await disposable.DisposeAsync();

            var vm = _serviceProvider.GetService<MainViewModel>();
            vm?.Dispose();

            _serviceProvider.Dispose();
        }

        base.OnExit(e);
    }
}
