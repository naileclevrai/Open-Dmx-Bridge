using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using ModernWpf;
using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services;

/// <summary>
/// Bascule les dictionnaires Themes/Light.xaml et Themes/Dark.xaml à chaud, aligne ModernWpf
/// (interrupteurs, listes, barres de défilement) et la barre de titre Windows (DWM), et suit le
/// réglage système quand la préférence est « Système ».
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
    private const string LightSource = "Themes/Light.xaml";
    private const string DarkSource = "Themes/Dark.xaml";
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private readonly ISettingsService _settings;
    private readonly ILoggingService _logger;

    public ThemeService(ISettingsService settings, ILoggingService logger)
    {
        _settings = settings;
        _logger = logger;
        Preference = settings.Current.Theme;
        Effective = Resolve(Preference);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public AppTheme Preference { get; private set; }
    public AppTheme Effective { get; private set; }
    public bool IsDark => Effective == AppTheme.Dark;

    public event EventHandler? ThemeChanged;

    public void Apply(AppTheme preference)
    {
        Preference = preference;
        _settings.Update(s => s.Theme = preference);
        _settings.Save();
        if (!ApplyEffective(Resolve(preference), log: true))
            ThemeChanged?.Invoke(this, EventArgs.Empty); // préférence changée, rendu identique : l'UI doit quand même se mettre à jour
    }

    public void Cycle() => Apply(Preference switch
    {
        AppTheme.System => AppTheme.Light,
        AppTheme.Light => AppTheme.Dark,
        _ => AppTheme.System
    });

    /// <summary>À appeler une fois au démarrage, avant la création de la fenêtre principale.</summary>
    public void ApplyStartup() => ApplyEffective(Effective, log: false, force: true);

    /// <summary>Barre de titre (boutons Windows) claire ou sombre pour une fenêtre donnée.</summary>
    public void ApplyToWindow(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            var dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch
        {
            // DWM indisponible : la barre de titre garde l'apparence par défaut.
        }
    }

    /// <returns>true si le thème effectif a changé (et que ThemeChanged a été levé).</returns>
    private bool ApplyEffective(AppTheme effective, bool log, bool force = false)
    {
        if (!force && effective == Effective)
            return false;

        Effective = effective;

        var app = Application.Current;
        if (app is null)
            return false;

        var merged = app.Resources.MergedDictionaries;
        var target = new Uri(effective == AppTheme.Dark ? DarkSource : LightSource, UriKind.Relative);
        var existing = merged.FirstOrDefault(d => d.Source is { } src &&
            (src.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)
             || src.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase)));

        var index = existing is null ? merged.Count : merged.IndexOf(existing);
        if (existing is not null)
            merged.RemoveAt(index);
        merged.Insert(index, new ResourceDictionary { Source = target });

        ThemeManager.Current.ApplicationTheme = effective == AppTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;

        foreach (Window window in app.Windows)
            ApplyToWindow(window);

        if (log)
            _logger.Info($"Apparence : {Describe(Preference)} → {(IsDark ? "sombre" : "claire")}.", nameof(ThemeService));

        ThemeChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Preference != AppTheme.System || e.Category != UserPreferenceCategory.General)
            return;

        Application.Current?.Dispatcher.BeginInvoke(() => ApplyEffective(Resolve(AppTheme.System), log: true));
    }

    private static AppTheme Resolve(AppTheme preference) => preference switch
    {
        AppTheme.Light => AppTheme.Light,
        AppTheme.Dark => AppTheme.Dark,
        _ => SystemPrefersDark() ? AppTheme.Dark : AppTheme.Light
    };

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    public static string Describe(AppTheme theme) => theme switch
    {
        AppTheme.Light => "Clair",
        AppTheme.Dark => "Sombre",
        _ => "Système"
    };

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
