using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Systems;
using HKLifeSim.Desktop.Services;

namespace HKLifeSim.Desktop.ViewModels;

internal sealed partial class SetupViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private IReadOnlyList<LegacyRecord> _savedLineage = [];

    [ObservableProperty]
    private EraCardInfo? _selectedEra;

    [ObservableProperty]
    private string _seedText = string.Empty;

    [ObservableProperty]
    private bool _canContinueLastSave;

    [ObservableProperty]
    private bool _hasCompletedLineage;

    [ObservableProperty]
    private bool _useLegacyMode;

    [ObservableProperty]
    private string? _errorMessage;

    public SetupViewModel(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);

        _main = main;
        EraCards = [.. main.Eras.Select(e => EraCatalog.Describe(e.EraId))];
        SelectedEra = EraCards.Count > 0 ? EraCards[0] : null;

        _ = InitializeAsync();
    }

    public IReadOnlyList<EraCardInfo> EraCards { get; }

    private async Task InitializeAsync()
    {
        var loaded = await _main.LoadAutosaveAsync().ConfigureAwait(true);
        if (loaded is null)
        {
            return;
        }

        var (state, lineage) = loaded.Value;
        CanContinueLastSave = state.IsAlive;
        _savedLineage = lineage;
        HasCompletedLineage = !state.IsAlive && lineage.Count > 0;
    }

    [RelayCommand]
    private async Task ContinueLastSaveAsync()
    {
        var loaded = await _main.LoadAutosaveAsync().ConfigureAwait(true);
        if (loaded is null || !loaded.Value.State.IsAlive)
        {
            return;
        }

        var (state, lineage) = loaded.Value;
        var era = _main.Eras.First(e => e.EraId == state.EraId);
        var chain = new GenerationChain(_main.Eras) { Lineage = [.. lineage] };
        _main.ShowGame(state, chain, era);
    }

    [RelayCommand]
    private void StartNewLife()
    {
        ErrorMessage = null;

        if (SelectedEra is null)
        {
            ErrorMessage = "請先揀一個年代。";
            return;
        }

        var era = _main.Eras.First(e => e.EraId == SelectedEra.EraId);
        var seed = ParseSeedOrRandom();
        var chain = new GenerationChain(_main.Eras);

        if (UseLegacyMode && HasCompletedLineage)
        {
            chain.Lineage = [.. _savedLineage];
        }

        try
        {
            var state = chain.StartNextGeneration(era, seed);
            _main.ShowGame(state, chain, era);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private int ParseSeedOrRandom() =>
        int.TryParse(SeedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
            ? seed
            : Environment.TickCount;
}
