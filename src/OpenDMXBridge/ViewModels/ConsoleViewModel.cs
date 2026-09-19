using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;
using OpenDMXBridge.Services.Dmx;

namespace OpenDMXBridge.ViewModels;

/// <summary>Un fader de la micro console, lié à un canal DMX.</summary>
public sealed partial class ConsoleFaderViewModel : ObservableObject
{
    private readonly DmxConsoleLayer _layer;
    private bool _syncing;

    [ObservableProperty] private int _channel;
    [ObservableProperty] private int _level;
    [ObservableProperty] private bool _isHeld;

    public ConsoleFaderViewModel(DmxConsoleLayer layer, int channel)
    {
        _layer = layer;
        _channel = channel;
        SyncFromLayer();
    }

    public string LevelPercent => $"{Level * 100 / 255} %";

    partial void OnLevelChanged(int value)
    {
        OnPropertyChanged(nameof(LevelPercent));
        if (_syncing)
            return;

        _layer.Set(Channel, (byte)Math.Clamp(value, 0, 255));
        IsHeld = true;
    }

    /// <summary>Rend le canal au flux Art-Net.</summary>
    [RelayCommand]
    public void Release()
    {
        _layer.Release(Channel);
        _syncing = true;
        try
        {
            Level = 0;
            IsHeld = false;
        }
        finally
        {
            _syncing = false;
        }
    }

    public void SetChannel(int channel)
    {
        Channel = channel;
        SyncFromLayer();
    }

    private void SyncFromLayer()
    {
        _syncing = true;
        try
        {
            IsHeld = _layer.IsHeld(Channel);
            Level = IsHeld ? _layer.Get(Channel) : 0;
        }
        finally
        {
            _syncing = false;
        }
    }
}

/// <summary>Micro console DMX : 16 faders paginés, grand master, blackout, mode de fusion.</summary>
public sealed partial class ConsoleViewModel : ObservableObject
{
    public const int FaderCount = 16;
    private const int MaxStartChannel = DmxConsoleLayer.SlotCount - FaderCount + 1;

    private readonly DmxConsoleLayer _layer;
    private readonly ILoggingService _logger;

    public ObservableCollection<ConsoleFaderViewModel> Faders { get; }
    public ObservableCollection<LabeledOption<ConsoleMergeMode>> MergeModes { get; }

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isBlackout;
    [ObservableProperty] private int _master = 255;
    [ObservableProperty] private int _startChannel = 1;
    [ObservableProperty] private LabeledOption<ConsoleMergeMode>? _selectedMergeMode;

    public ConsoleViewModel(IDmxEngine dmxEngine, ILoggingService logger)
    {
        _layer = dmxEngine.Console;
        _logger = logger;

        Faders = new ObservableCollection<ConsoleFaderViewModel>();
        for (var i = 0; i < FaderCount; i++)
            Faders.Add(new ConsoleFaderViewModel(_layer, StartChannel + i));

        MergeModes =
        [
            new LabeledOption<ConsoleMergeMode>(ConsoleMergeMode.Htp, "HTP — le plus haut gagne"),
            new LabeledOption<ConsoleMergeMode>(ConsoleMergeMode.Override, "Override — la console écrase")
        ];
        SelectedMergeMode = MergeModes[0];
    }

    public string PageDisplay => $"Canaux {StartChannel} à {StartChannel + FaderCount - 1}";
    public string MasterPercent => $"{Master * 100 / 255} %";
    public string HeldSummary => _layer.HeldCount switch
    {
        0 => "Aucun canal tenu",
        1 => "1 canal tenu",
        var n => $"{n} canaux tenus"
    };

    partial void OnIsEnabledChanged(bool value)
    {
        _layer.Enabled = value;
        _logger.Info(value ? "Console DMX activée." : "Console DMX désactivée — l'Art-Net reprend la main.", nameof(ConsoleViewModel));
    }

    partial void OnIsBlackoutChanged(bool value)
    {
        _layer.Blackout = value;
        if (value)
            _logger.Warning("Blackout console : toute la trame DMX est à zéro.", nameof(ConsoleViewModel));
    }

    partial void OnMasterChanged(int value)
    {
        _layer.Master = value;
        OnPropertyChanged(nameof(MasterPercent));
    }

    partial void OnSelectedMergeModeChanged(LabeledOption<ConsoleMergeMode>? value)
    {
        if (value is not null)
            _layer.Mode = value.Value;
    }

    partial void OnStartChannelChanged(int value)
    {
        var clamped = Math.Clamp(value, 1, MaxStartChannel);
        if (clamped != value)
        {
            StartChannel = clamped;
            return;
        }

        for (var i = 0; i < Faders.Count; i++)
            Faders[i].SetChannel(clamped + i);

        OnPropertyChanged(nameof(PageDisplay));
    }

    [RelayCommand]
    private void PreviousPage() => StartChannel = Math.Max(1, StartChannel - FaderCount);

    [RelayCommand]
    private void NextPage() => StartChannel = Math.Min(MaxStartChannel, StartChannel + FaderCount);

    /// <summary>Rend tous les canaux (toute la console, pas seulement la page visible).</summary>
    [RelayCommand]
    private void ReleaseAll()
    {
        _layer.ReleaseAll();
        foreach (var fader in Faders)
            fader.Release();
        RefreshSummary();
    }

    [RelayCommand]
    private void FullPage()
    {
        foreach (var fader in Faders)
            fader.Level = 255;
        RefreshSummary();
    }

    public void RefreshSummary() => OnPropertyChanged(nameof(HeldSummary));
}
