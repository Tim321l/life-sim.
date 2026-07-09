using CommunityToolkit.Mvvm.Input;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Systems;
using HKLifeSim.Desktop.Services;

namespace HKLifeSim.Desktop.ViewModels;

internal sealed partial class ObituaryViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly GenerationChain _chain;
    private readonly EraConfig _era;
    private readonly GameState _state;

    public ObituaryViewModel(MainViewModel main, GameState state, LegacyRecord legacy, GenerationChain chain, EraConfig era)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(era);

        _main = main;
        _state = state;
        _chain = chain;
        _era = era;

        Age = state.Age;
        TranslatedDeathCause = DeathCauseTranslator.Translate(state.DeathCause);
        Stats = state.Stats;
        FlagChips = [.. state.FlagsSet];
        EventsExperienced = state.EventHistory.Count;
        Legacy = legacy;
    }

    public int Age { get; }

    public string TranslatedDeathCause { get; }

    public StatBlock Stats { get; }

    public IReadOnlyList<string> FlagChips { get; }

    public int EventsExperienced { get; }

    public LegacyRecord Legacy { get; }

    [RelayCommand]
    private void StartNextGeneration()
    {
        var seed = _state.RngSeed + 1;
        var nextState = _chain.StartNextGeneration(_era, seed);
        _main.ShowGame(nextState, _chain, _era);
    }

    [RelayCommand]
    private void Restart() => _main.ShowSetup();

    [RelayCommand]
    private static void Exit() => MainViewModel.Exit();
}
