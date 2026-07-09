using CommunityToolkit.Mvvm.ComponentModel;
using HKLifeSim.Core.Data;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;
using HKLifeSim.Core.Persistence;
using HKLifeSim.Core.Systems;

namespace HKLifeSim.Desktop.ViewModels;

internal sealed partial class MainViewModel : ViewModelBase
{
    private const string AutosaveSlot = "autosave";

    private readonly SaveManager _saveManager;
    private readonly Dictionary<string, IReadOnlyList<GameEvent>> _eventsByEra;

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    public MainViewModel()
    {
        var baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HKLifeSim");
        var store = new FileSaveStore(baseDirectory);
        _saveManager = new SaveManager(store, TimeProvider.System);

        try
        {
            var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
            Eras = EraRepository.Load(File.ReadAllText(Path.Combine(dataDirectory, "eras.json")));
            _eventsByEra = Eras.ToDictionary(era => era.EraId, era => LoadEventsFor(dataDirectory, era));
        }
        catch (EventDataException ex)
        {
            Eras = [];
            _eventsByEra = [];
            LoadErrorMessage = ex.Message;
        }

        CurrentViewModel = LoadErrorMessage is null ? new SetupViewModel(this) : null;
    }

    public IReadOnlyList<EraConfig> Eras { get; }

    public string? LoadErrorMessage { get; }

    public IReadOnlyList<GameEvent> EventsFor(EraConfig era)
    {
        ArgumentNullException.ThrowIfNull(era);
        return _eventsByEra[era.EraId];
    }

    public void ShowSetup() => CurrentViewModel = new SetupViewModel(this);

    public void ShowGame(GameState state, GenerationChain chain, EraConfig era) =>
        CurrentViewModel = new GameViewModel(this, state, chain, era);

    public void ShowObituary(GameState state, LegacyRecord legacy, GenerationChain chain, EraConfig era) =>
        CurrentViewModel = new ObituaryViewModel(this, state, legacy, chain, era);

    public async Task AutosaveAsync(GameState state, IReadOnlyList<LegacyRecord> lineage) =>
        await _saveManager.SaveAsync(state, AutosaveSlot, lineage).ConfigureAwait(true);

    public async Task<(GameState State, IReadOnlyList<LegacyRecord> Lineage)?> LoadAutosaveAsync()
    {
        var state = await _saveManager.LoadAsync(AutosaveSlot).ConfigureAwait(true);
        if (state is null)
        {
            return null;
        }

        var lineage = await _saveManager.LoadLineageAsync(AutosaveSlot).ConfigureAwait(true);
        return (state, lineage);
    }

    public static void Exit() => Environment.Exit(0);

    private static IReadOnlyList<GameEvent> LoadEventsFor(string dataDirectory, EraConfig era)
    {
        var files = era.EventPoolFiles.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(dataDirectory, f)));
        return EventRepository.Load(files, [era]);
    }
}
