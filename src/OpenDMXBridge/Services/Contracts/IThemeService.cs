using OpenDMXBridge.Models;

namespace OpenDMXBridge.Services.Contracts;

/// <summary>Gestion de l'apparence : préférence (Système, Clair, Sombre) et thème effectivement appliqué.</summary>
public interface IThemeService
{
    /// <summary>Préférence de l'utilisateur.</summary>
    AppTheme Preference { get; }

    /// <summary>Thème réellement affiché (Clair ou Sombre, jamais Système).</summary>
    AppTheme Effective { get; }

    bool IsDark { get; }

    /// <summary>Déclenché après chaque changement du thème effectif.</summary>
    event EventHandler? ThemeChanged;

    /// <summary>Applique une préférence et la mémorise.</summary>
    void Apply(AppTheme preference);

    /// <summary>Passe à la préférence suivante : Système → Clair → Sombre → Système.</summary>
    void Cycle();
}
