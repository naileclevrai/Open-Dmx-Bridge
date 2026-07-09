using System.IO;
using System.Text.Json;
using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    private readonly ILoggingService _logger;
    private readonly object _lock = new();
    private AppSettings _current = new();

    public SettingsService(ILoggingService logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "OpenDMXBridge");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
    }

    public AppSettings Current
    {
        get
        {
            lock (_lock)
                return _current;
        }
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_settingsPath))
            {
                _current = new AppSettings();
                return;
            }

            try
            {
                var json = File.ReadAllText(_settingsPath);
                _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                _logger.Info("Paramètres chargés.", nameof(SettingsService));
            }
            catch (Exception ex)
            {
                _current = new AppSettings();
                _logger.Warning($"Impossible de charger les paramètres : {ex.Message}", nameof(SettingsService));
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_current, JsonOptions);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                _logger.Error($"Échec sauvegarde paramètres : {ex.Message}", nameof(SettingsService));
            }
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        lock (_lock)
        {
            mutate(_current);
            SettingsChanged?.Invoke(this, _current);
        }
    }
}
