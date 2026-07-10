using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HKLifeSim.Core.Presentation;

namespace HKLifeSim.Desktop.Views;

internal sealed partial class DigiviceView : UserControl, IDisposable
{
    private const string AssetBaseUri = "avares://HKLifeSim.Desktop/Assets/Sprites";
    private const int CellSize = 48;
    private const int IconCellSize = 16;
    private const int FallbackHappyDurationMs = 2000;
    private const int EvolveFlashDurationMs = 800;
    private const int EvolveFlashIntervalMs = 200;
    private const int DeathFadeDurationMs = 800;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    public static readonly StyledProperty<Stage> CurrentStageProperty =
        AvaloniaProperty.Register<DigiviceView, Stage>(nameof(CurrentStage));

    public static readonly StyledProperty<Mood> CurrentMoodProperty =
        AvaloniaProperty.Register<DigiviceView, Mood>(nameof(CurrentMood));

    public static readonly StyledProperty<string> HeaderTextProperty =
        AvaloniaProperty.Register<DigiviceView, string>(nameof(HeaderText), string.Empty);

    private readonly SpriteManifest _manifest;
    private readonly Dictionary<string, Dictionary<(string PoseKey, int Frame), CroppedBitmap>> _stageFrameCache = [];
    private readonly Dictionary<string, Dictionary<int, CroppedBitmap>> _iconFrameCache = [];
    private readonly List<IDisposable> _loadedResources = [];
    private readonly DispatcherTimer _timer;

    private Bitmap? _tombstoneBitmap;
    private PlaybackPhase _phase = PlaybackPhase.Idle;
    private DateTime _phaseStartUtc = DateTime.UtcNow;
    private ActionDef? _currentActionDef;

    // The stage actually being displayed right now, which lags CurrentStage during an
    // Evolving/DeathFade transition; null until the first render establishes it.
    private Stage? _renderedStage;

    public DigiviceView()
    {
        InitializeComponent();

        var manifestJson = ReadEmbeddedText($"{AssetBaseUri}/manifest.json");
        _manifest = SpriteManifestRepository.Load(manifestJson);

        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += OnTick;

        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => Dispose();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;

        foreach (var resource in _loadedResources)
        {
            resource.Dispose();
        }

        _loadedResources.Clear();
        _stageFrameCache.Clear();
        _iconFrameCache.Clear();

        // Already disposed via _loadedResources above (Bitmap.Dispose is idempotent); disposed
        // again explicitly here so static analysis can see this field is accounted for.
        _tombstoneBitmap?.Dispose();
        _tombstoneBitmap = null;
    }

    public Stage CurrentStage
    {
        get => GetValue(CurrentStageProperty);
        set => SetValue(CurrentStageProperty, value);
    }

    public Mood CurrentMood
    {
        get => GetValue(CurrentMoodProperty);
        set => SetValue(CurrentMoodProperty, value);
    }

    public string HeaderText
    {
        get => GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    // Plays a one-shot action animation for the given Core Activity id or "web:*" key. Falls
    // back to the generic happy overlay (per SpriteTimeline.ResolveAction) when the key isn't
    // mapped in the manifest.
    public void PlayAction(string activityKey)
    {
        ArgumentNullException.ThrowIfNull(activityKey);

        _currentActionDef = SpriteTimeline.ResolveAction(_manifest, activityKey);
        _phase = _currentActionDef is null ? PlaybackPhase.Happy : PlaybackPhase.Action;
        _phaseStartUtc = DateTime.UtcNow;
        RenderFrame();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CurrentStageProperty || change.Property == CurrentMoodProperty)
        {
            OnAppearanceChanged();
        }
    }

    private void OnAppearanceChanged()
    {
        // Dead overrides everything immediately, interrupting any in-flight action/happy/evolve
        // playback (§B: 死亡壓過一切).
        if (CurrentStage == Stage.Tombstone && _renderedStage != Stage.Tombstone)
        {
            _phase = PlaybackPhase.DeathFade;
            _phaseStartUtc = DateTime.UtcNow;
            return;
        }

        // Action/happy playback isn't interrupted by a mood/stage change mid-flight (§D: "action
        // 播放期間 mood 判定暫停"); it resumes reflecting the new appearance once that phase ends.
        if (_phase != PlaybackPhase.Idle)
        {
            return;
        }

        if (_renderedStage.HasValue && _renderedStage != CurrentStage)
        {
            _phase = PlaybackPhase.Evolving;
            _phaseStartUtc = DateTime.UtcNow;
            return;
        }

        _renderedStage = CurrentStage;
        _phaseStartUtc = DateTime.UtcNow;
        RenderFrame();
    }

    private void OnTick(object? sender, EventArgs e) => RenderFrame();

    private void RenderFrame()
    {
        var elapsedMs = (long)(DateTime.UtcNow - _phaseStartUtc).TotalMilliseconds;

        if (_phase == PlaybackPhase.DeathFade)
        {
            RenderDeathFade(elapsedMs);
            return;
        }

        if (_phase == PlaybackPhase.Evolving)
        {
            RenderEvolving(elapsedMs);
            return;
        }

        if (_renderedStage is null)
        {
            _renderedStage = CurrentStage;
        }

        if (_renderedStage == Stage.Tombstone)
        {
            RenderTombstoneFrame();
            return;
        }

        var stageKey = StageKeyFor(_renderedStage.Value);
        if (!_manifest.Stages.TryGetValue(stageKey, out var stageSheet))
        {
            return;
        }

        switch (_phase)
        {
            case PlaybackPhase.Action when _currentActionDef is { } action:
                RenderAction(stageSheet, action, elapsedMs);
                break;
            case PlaybackPhase.Happy:
                RenderHappy(stageSheet, elapsedMs);
                break;
            default:
                RenderIdle(stageSheet, elapsedMs);
                break;
        }
    }

    // Flashes the outgoing stage's sprite twice, then swaps to the new stage (§B: 進化演出：閃屏×2 再換 sprite).
    private void RenderEvolving(long elapsedMs)
    {
        if (_renderedStage is { } stage && stage != Stage.Tombstone)
        {
            var stageKey = StageKeyFor(stage);
            if (_manifest.Stages.TryGetValue(stageKey, out var stageSheet) && stageSheet.Poses.TryGetValue("stand", out var pose))
            {
                SetBaseFrame(stageSheet, "stand", pose, 0);
            }
        }

        BaseImage.Opacity = elapsedMs / EvolveFlashIntervalMs % 2 == 0 ? 1.0 : 0.1;

        if (elapsedMs >= EvolveFlashDurationMs)
        {
            BaseImage.Opacity = 1.0;
            _renderedStage = CurrentStage;
            _phase = PlaybackPhase.Idle;
            _phaseStartUtc = DateTime.UtcNow;
        }
    }

    private void RenderDeathFade(long elapsedMs)
    {
        if (_renderedStage is { } stage && stage != Stage.Tombstone)
        {
            var stageKey = StageKeyFor(stage);
            if (_manifest.Stages.TryGetValue(stageKey, out var stageSheet) && stageSheet.Poses.TryGetValue("stand", out var pose))
            {
                SetBaseFrame(stageSheet, "stand", pose, 0);
            }
        }

        var progress = Math.Min(1.0, elapsedMs / (double)DeathFadeDurationMs);
        BaseImage.Opacity = 1.0 - progress;

        if (elapsedMs >= DeathFadeDurationMs)
        {
            BaseImage.Opacity = 1.0;
            _renderedStage = Stage.Tombstone;
            _phase = PlaybackPhase.Idle;
            _phaseStartUtc = DateTime.UtcNow;
        }
    }

    private void RenderAction(StageSheet stageSheet, ActionDef action, long elapsedMs)
    {
        if (!stageSheet.Poses.TryGetValue(action.Pose, out var pose))
        {
            _phase = PlaybackPhase.Idle;
            return;
        }

        var icon = action.Icon is not null && _manifest.Icons.TryGetValue(action.Icon, out var iconDef) ? iconDef : null;
        var frame = SpriteTimeline.GetActionFrame(action, pose, icon, elapsedMs);

        SetBaseFrame(stageSheet, action.Pose, pose, frame.PoseFrame);
        SetIconFrame(action.Icon, icon, frame.IconFrame);

        if (frame.IsFinished)
        {
            _phase = PlaybackPhase.Happy;
            _phaseStartUtc = DateTime.UtcNow;
        }
    }

    private void RenderHappy(StageSheet stageSheet, long elapsedMs)
    {
        if (stageSheet.Poses.TryGetValue("stand", out var pose))
        {
            var frameIndex = SpriteTimeline.GetLoopingFrame(pose.Frames, pose.Ms, elapsedMs);
            SetBaseFrame(stageSheet, "stand", pose, frameIndex);
            SetIconFrame(null, null, null);
        }

        if (elapsedMs >= FallbackHappyDurationMs)
        {
            _phase = PlaybackPhase.Idle;
            _phaseStartUtc = DateTime.UtcNow;
        }
    }

    private void RenderIdle(StageSheet stageSheet, long elapsedMs)
    {
        var poseKey = PoseKeyForMood(CurrentMood);
        if (!stageSheet.Poses.TryGetValue(poseKey, out var pose) && !stageSheet.Poses.TryGetValue("stand", out pose))
        {
            return;
        }

        var resolvedPoseKey = stageSheet.Poses.ContainsKey(poseKey) ? poseKey : "stand";
        var frameIndex = SpriteTimeline.GetLoopingFrame(pose.Frames, pose.Ms, elapsedMs);
        SetBaseFrame(stageSheet, resolvedPoseKey, pose, frameIndex);
        SetIconFrame(null, null, null);
    }

    private void RenderTombstoneFrame()
    {
        if (_tombstoneBitmap is null)
        {
            _tombstoneBitmap = new Bitmap(AssetLoader.Open(new Uri($"{AssetBaseUri}/tombstone.png")));
            _loadedResources.Add(_tombstoneBitmap);
        }

        BaseImage.Source = _tombstoneBitmap;
        IconImage.IsVisible = false;
    }

    private void SetBaseFrame(StageSheet stageSheet, string poseKey, PoseDef pose, int frameIndex)
    {
        var cache = GetOrLoadStageCache(stageSheet);
        if (cache.TryGetValue((poseKey, frameIndex), out var bitmap))
        {
            BaseImage.Source = bitmap;
        }
    }

    private void SetIconFrame(string? iconKey, IconDef? icon, int? frameIndex)
    {
        if (iconKey is null || icon is null || frameIndex is null)
        {
            IconImage.IsVisible = false;
            return;
        }

        var cache = GetOrLoadIconCache(iconKey, icon);
        if (cache.TryGetValue(frameIndex.Value, out var bitmap))
        {
            IconImage.Source = bitmap;
            IconImage.IsVisible = true;
        }
    }

    // All frames for a stage sheet are cropped once, on first use, and cached — the DispatcherTimer
    // tick handler only ever swaps Image.Source to an already-cached CroppedBitmap, never allocates.
    private Dictionary<(string PoseKey, int Frame), CroppedBitmap> GetOrLoadStageCache(StageSheet stageSheet)
    {
        if (_stageFrameCache.TryGetValue(stageSheet.Sheet, out var cached))
        {
            return cached;
        }

        var bitmap = new Bitmap(AssetLoader.Open(new Uri($"{AssetBaseUri}/{stageSheet.Sheet}")));
        _loadedResources.Add(bitmap);

        var frames = new Dictionary<(string, int), CroppedBitmap>();
        foreach (var (poseKey, pose) in stageSheet.Poses)
        {
            for (var frame = 0; frame < pose.Frames; frame++)
            {
                var rect = new PixelRect(frame * CellSize, pose.Row * CellSize, CellSize, CellSize);
                var cropped = new CroppedBitmap(bitmap, rect);
                frames[(poseKey, frame)] = cropped;
                _loadedResources.Add(cropped);
            }
        }

        _stageFrameCache[stageSheet.Sheet] = frames;
        return frames;
    }

    private Dictionary<int, CroppedBitmap> GetOrLoadIconCache(string iconKey, IconDef icon)
    {
        if (_iconFrameCache.TryGetValue(iconKey, out var cached))
        {
            return cached;
        }

        var bitmap = new Bitmap(AssetLoader.Open(new Uri($"{AssetBaseUri}/{icon.File}")));
        _loadedResources.Add(bitmap);

        var frames = new Dictionary<int, CroppedBitmap>();
        for (var frame = 0; frame < icon.Frames; frame++)
        {
            var cropped = new CroppedBitmap(bitmap, new PixelRect(frame * IconCellSize, 0, IconCellSize, IconCellSize));
            frames[frame] = cropped;
            _loadedResources.Add(cropped);
        }

        _iconFrameCache[iconKey] = frames;
        return frames;
    }

    private static string ReadEmbeddedText(string avaresUri)
    {
        using var stream = AssetLoader.Open(new Uri(avaresUri));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string StageKeyFor(Stage stage) => stage switch
    {
        Stage.Baby => "baby",
        Stage.Child => "child",
        Stage.Teen => "teen",
        Stage.Adult => "adult",
        Stage.Elder => "elder",
        _ => "adult",
    };

    // Idle/Happy both use "stand" — there is no dedicated happy artwork in the locked asset plan
    // (see P7.2's commit message); AppearanceCalculator itself never returns Mood.Happy.
    private static string PoseKeyForMood(Mood mood) => mood switch
    {
        Mood.Sick => "sick",
        Mood.Stressed => "stressed",
        Mood.Tired => "tired",
        _ => "stand",
    };

    private enum PlaybackPhase
    {
        Idle,
        Action,
        Happy,
        Evolving,
        DeathFade,
    }
}
