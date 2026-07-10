using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;
using HKLifeSim.Core.Presentation;
using HKLifeSim.Core.Systems;
using HKLifeSim.Desktop.Services;

namespace HKLifeSim.Desktop.ViewModels;

internal sealed record ChoiceOption(string Id, string Text);

internal sealed partial class GameViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly GenerationChain _chain;
    private readonly EraConfig _era;
    private readonly EventEngine _engine;
    private readonly LifecycleSystem _lifecycle;
    private readonly GameState _state;
    private GameEvent _currentEvent;

    [ObservableProperty]
    private int _age;

    [ObservableProperty]
    private int _year;

    [ObservableProperty]
    private string _moneyDisplay = string.Empty;

    [ObservableProperty]
    private int _healthValue;

    [ObservableProperty]
    private IBrush _healthColor = Brushes.Gray;

    [ObservableProperty]
    private int _stressValue;

    [ObservableProperty]
    private IBrush _stressColor = Brushes.Gray;

    [ObservableProperty]
    private int _familyBondValue;

    [ObservableProperty]
    private int _educationValue;

    [ObservableProperty]
    private int _reputationValue;

    [ObservableProperty]
    private string _eventTitle = string.Empty;

    [ObservableProperty]
    private string _eventBody = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ChoiceOption> _choices = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statDeltaToast;

    [ObservableProperty]
    private Stage _characterStage;

    [ObservableProperty]
    private Mood _characterMood;

    [ObservableProperty]
    private string _characterHeaderText = string.Empty;

    public GameViewModel(MainViewModel main, GameState state, GenerationChain chain, EraConfig era)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(era);

        _main = main;
        _state = state;
        _chain = chain;
        _era = era;

        var events = main.EventsFor(era);
        _engine = new EventEngine(events, era, state.RngSeed);
        _lifecycle = new LifecycleSystem(state.RngSeed);
        _currentEvent = _engine.SelectNextEvent(_state);

        RefreshStatBar();
        RefreshEventDisplay();
    }

    [RelayCommand]
    private async Task SelectChoiceAsync(ChoiceOption? choice)
    {
        if (IsBusy || choice is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var before = _state.Stats;
            _engine.ApplyChoice(_state, _currentEvent, choice.Id);
            StatDeltaToast = BuildDeltaToast(before, _state.Stats);
            await _main.AutosaveAsync(_state, _chain.Lineage).ConfigureAwait(true);

            if (_state.IsAlive)
            {
                _lifecycle.AdvanceYear(_state, _era);
                await _main.AutosaveAsync(_state, _chain.Lineage).ConfigureAwait(true);
            }

            if (!_state.IsAlive)
            {
                var legacy = LegacySystem.GenerateLegacy(_state);
                _chain.Lineage.Add(legacy);
                await _main.AutosaveAsync(_state, _chain.Lineage).ConfigureAwait(true);
                _main.ShowObituary(_state, legacy, _chain, _era);
                return;
            }

            _currentEvent = _engine.SelectNextEvent(_state);
            RefreshStatBar();
            RefreshEventDisplay();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshEventDisplay()
    {
        EventTitle = _currentEvent.Title;
        EventBody = _currentEvent.Body;
        Choices = new ObservableCollection<ChoiceOption>(_currentEvent.Choices.Select(c => new ChoiceOption(c.Id, c.Text)));
    }

    private void RefreshStatBar()
    {
        Age = _state.Age;
        Year = _state.CurrentYear;
        MoneyDisplay = FormatMoney(_state.Stats.Money);
        HealthValue = _state.Stats.Health;
        HealthColor = StatColorScale.ForStat(_state.Stats.Health, highIsGood: true);
        StressValue = _state.Stats.Stress;
        StressColor = StatColorScale.ForStat(_state.Stats.Stress, highIsGood: false);
        FamilyBondValue = _state.Stats.FamilyBond;
        EducationValue = _state.Stats.Education;
        ReputationValue = _state.Stats.Reputation;

        var appearance = AppearanceCalculator.Calculate(_state);
        CharacterStage = appearance.Stage;
        CharacterMood = appearance.Mood;
        CharacterHeaderText = $"{_state.Age} 歲 · {_state.Profile?.Name ?? "香港仔"}";
    }

    private static string FormatMoney(int money)
    {
        var sign = money < 0 ? "-" : string.Empty;
        return $"{sign}${Math.Abs(money):N0}";
    }

    private static string BuildDeltaToast(StatBlock before, StatBlock after)
    {
        List<string> parts =
        [
            .. DeltaText("金錢", after.Money - before.Money),
            .. DeltaText("健康", after.Health - before.Health),
            .. DeltaText("壓力", after.Stress - before.Stress),
            .. DeltaText("親情", after.FamilyBond - before.FamilyBond),
            .. DeltaText("學歷", after.Education - before.Education),
            .. DeltaText("聲望", after.Reputation - before.Reputation),
        ];

        return string.Join("  ", parts);
    }

    private static IEnumerable<string> DeltaText(string label, int delta)
    {
        if (delta == 0)
        {
            yield break;
        }

        var sign = delta > 0 ? "+" : string.Empty;
        yield return $"{label} {sign}{delta}";
    }
}
