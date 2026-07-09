using OpenDMXBridge.Models;

namespace OpenDMXBridge.Services.Contracts;

public interface ISettingsService
{
    AppSettings Current { get; }
    event EventHandler<AppSettings>? SettingsChanged;

    void Load();
    void Save();
    void Update(Action<AppSettings> mutate);
}
