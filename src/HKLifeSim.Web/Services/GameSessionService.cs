using HKLifeSim.Core.Data;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;
using HKLifeSim.Core.Persistence;
using HKLifeSim.Core.Systems;
using Microsoft.JSInterop;

namespace HKLifeSim.Web.Services;

internal sealed class GameSessionService
{
    private const string AutosaveSlot = "autosave";

    private readonly HttpClient _http;
    private readonly SaveManager _saveManager;
    private readonly Dictionary<string, IReadOnlyList<GameEvent>> _eventsByEra = [];

    private EventEngine? _engine;
    private LifecycleSystem? _lifecycle;

    public GameSessionService(HttpClient http, ISaveStore store)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(store);

        _http = http;
        _saveManager = new SaveManager(store, TimeProvider.System);
    }

    public event Action? Changed;

    public IReadOnlyList<EraConfig> Eras { get; private set; } = [];

    public bool IsLoaded { get; private set; }

    public string? LoadErrorMessage { get; private set; }

    public EraConfig? Era { get; private set; }

    public GenerationChain? Chain { get; private set; }

    public GameState? State { get; private set; }

    public GameEvent? CurrentEvent { get; private set; }

    public bool IsBusy { get; private set; }

    public string? StatDeltaToast { get; private set; }

    public string? SaveErrorMessage { get; private set; }

    public LegacyRecord? LastLegacy { get; private set; }

    public async Task InitializeAsync()
    {
        if (IsLoaded)
        {
            return;
        }

        try
        {
            var erasJson = await _http.GetStringAsync(new Uri("data/eras.json", UriKind.Relative)).ConfigureAwait(false);
            Eras = EraRepository.Load(erasJson);

            foreach (var era in Eras)
            {
                var files = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var file in era.EventPoolFiles)
                {
                    var fileUri = new Uri($"data/{file}", UriKind.Relative);
                    files[file] = await _http.GetStringAsync(fileUri).ConfigureAwait(false);
                }

                _eventsByEra[era.EraId] = EventRepository.Load(files, [era]);
            }
        }
        catch (EventDataException ex)
        {
            LoadErrorMessage = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            LoadErrorMessage = $"無法載入遊戲內容 (Failed to load game content): {ex.Message}";
        }
        finally
        {
            IsLoaded = true;
            Changed?.Invoke();
        }
    }

    public async Task<bool> TryContinueLastSaveAsync()
    {
        var loaded = await LoadAutosaveAsync().ConfigureAwait(false);
        if (loaded is null || !loaded.Value.State.IsAlive)
        {
            return false;
        }

        var (state, lineage) = loaded.Value;
        Era = Eras.First(e => e.EraId == state.EraId);
        Chain = new GenerationChain(Eras) { Lineage = [.. lineage] };
        State = state;
        SetUpEngine();
        Changed?.Invoke();
        return true;
    }

    public async Task<(GameState State, IReadOnlyList<LegacyRecord> Lineage)?> LoadAutosaveAsync()
    {
        var state = await _saveManager.LoadAsync(AutosaveSlot).ConfigureAwait(false);
        if (state is null)
        {
            return null;
        }

        var lineage = await _saveManager.LoadLineageAsync(AutosaveSlot).ConfigureAwait(false);
        return (state, lineage);
    }

    public void StartNewLife(EraConfig era, int seed, IReadOnlyList<LegacyRecord>? lineage, string? name = null, Gender gender = Gender.Other)
    {
        ArgumentNullException.ThrowIfNull(era);

        Era = era;
        Chain = new GenerationChain(Eras);
        if (lineage is not null)
        {
            Chain.Lineage = [.. lineage];
        }

        State = Chain.StartNextGeneration(era, seed);
        RollFamilyBackground();
        
        var charName = string.IsNullOrWhiteSpace(name) ? "香港仔" : name.Trim();
        State.Profile = new CharacterProfile(charName, gender, State.CurrentYear - 18);

        SetUpEngine();
        Changed?.Invoke();
    }

    public void StartNextGeneration()
    {
        if (Chain is null || Era is null || State is null)
        {
            return;
        }

        var seed = State.RngSeed + 1;
        State = Chain.StartNextGeneration(Era, seed);
        RollFamilyBackground();
        
        // Carry over name/gender if profile exists, otherwise generic
        var existingProfile = Chain.Lineage.Count > 0 ? State.Profile : null;
        var nextName = existingProfile?.Name ?? "香港仔";
        var nextGender = existingProfile?.Gender ?? Gender.Other;
        State.Profile = new CharacterProfile(nextName, nextGender, State.CurrentYear - 18);

        SetUpEngine();
        StatDeltaToast = null;
        Changed?.Invoke();
    }

    private void RollFamilyBackground()
    {
        if (State is null) return;
        
        State.FlagsSet.Remove("family_poor");
        State.FlagsSet.Remove("family_middle");
        State.FlagsSet.Remove("family_rich");

        var roll = Random.Shared.Next(100);
        if (roll < 35) // 35% poor
        {
            State.SetFlag("family_poor");
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: -(int)(State.Stats.Money * 0.8)));
        }
        else if (roll < 85) // 50% middle
        {
            State.SetFlag("family_middle");
        }
        else // 15% rich
        {
            State.SetFlag("family_rich");
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: State.Stats.Money * 2));
        }
    }

    public async Task SelectChoiceAsync(string choiceId)
    {
        ArgumentNullException.ThrowIfNull(choiceId);

        if (IsBusy || State is null || CurrentEvent is null || _engine is null || _lifecycle is null || Era is null || Chain is null)
        {
            return;
        }

        IsBusy = true;
        Changed?.Invoke();
        try
        {
            var before = State.Stats;
            _engine.ApplyChoice(State, CurrentEvent, choiceId);
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);

            if (!State.IsAlive)
            {
                LastLegacy = LegacySystem.GenerateLegacy(State);
                Chain.Lineage.Add(LastLegacy);
                await AutosaveAsync().ConfigureAwait(false);
            }
            else
            {
                // Enter active action phase for this year
                State.SetFlag("event_resolved_for_year");
                CurrentEvent = null;
                await AutosaveAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    public string? MilestoneMessage { get; set; }

    public async Task AdvanceYearAsync()
    {
        if (IsBusy || State is null || Era is null || Chain is null || _lifecycle is null)
        {
            return;
        }

        IsBusy = true;
        Changed?.Invoke();
        try
        {
            // Clear resolved flag and all active action limits/history flags
            State.FlagsSet.Remove("event_resolved_for_year");
            var actionFlags = State.FlagsSet.Where(f => f.StartsWith("action_", StringComparison.Ordinal)).ToList();
            foreach (var flag in actionFlags)
            {
                State.FlagsSet.Remove(flag);
            }

            MilestoneMessage = _lifecycle.AdvanceYear(State, Era);
            await AutosaveAsync().ConfigureAwait(false);

            if (!State.IsAlive)
            {
                LastLegacy = LegacySystem.GenerateLegacy(State);
                Chain.Lineage.Add(LastLegacy);
                await AutosaveAsync().ConfigureAwait(false);
            }
            else
            {
                RollWorldNewsEvent();
                CurrentEvent = _engine?.SelectNextEvent(State);
                StatDeltaToast = null;
                await AutosaveAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    public async Task<string> PlayGamesAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_play_") >= 2) return "玩得太耐啦，要去溫書做功課啦！";

        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Stress: -5, Health: 2, FamilyBond: -1));
        AddActionFlag("action_play_");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return "你開心地打機/玩玩具，放鬆咗心情，但冇時間陪屋企人。";
    }

    public async Task<string> AskAllowanceAsync()
    {
        if (State is null) return "無效狀態";
        if (State.HasFlag("action_allowance")) return "今年已經攞過零用錢啦，唔好貪心！";

        var before = State.Stats;
        State.SetFlag("action_allowance");
        
        var success = Random.Shared.Next(100) < 70;
        if (success)
        {
            var baseCash = 100;
            if (State.HasFlag("family_poor")) baseCash = 20;
            else if (State.HasFlag("family_rich")) baseCash = 1000;

            var cash = ScaleMoney(baseCash);
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cash, FamilyBond: -2));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"父母雖然碎碎念，但都俾咗零用錢你！(獲得 ${cash:N0})";
        }
        else
        {
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Stress: 2));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return "父母拒絕咗你，仲話你要慳啲使，你覺得有啲委屈。";
        }
    }

    public string ExportShareCode()
    {
        if (State is null) return string.Empty;
        var raw = $"{State.Profile?.Name ?? "香港仔"}|{(int)(State.Profile?.Gender ?? Gender.Other)}|{State.Age}|{State.Stats.Money}|{State.Stats.Reputation}|{State.Stats.Education}|{State.EraId}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        return Convert.ToBase64String(bytes);
    }

    public string ImportShareCode(string code)
    {
        if (State is null) return "無效狀態";
        try
        {
            var bytes = Convert.FromBase64String(code);
            var raw = System.Text.Encoding.UTF8.GetString(bytes);
            var parts = raw.Split('|');
            if (parts.Length < 7) return "無效嘅分享代碼";

            var name = parts[0];
            var genderVal = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            var age = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            var money = int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
            var reputation = int.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
            var education = int.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture);
            var eraId = parts[6];

            var friendFlag = $"friend_{name}_{age}_{money}_{reputation}_{education}_{eraId}";
            State.SetFlag(friendFlag);
            return $"成功加好友：{name} (年齡: {age}歲, 財產: ${money:N0}, Era: {eraId})！";
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException)
        {
            return "解析分享代碼失敗，格式唔正確！";
        }
    }

    // --- Active Actions Helpers & Methods ---

    public int ScaleMoney(int moneyAmount)
    {
        if (Era is null)
        {
            return moneyAmount;
        }
        var delta = InflationScaler.Scale(new StatDelta(Money: moneyAmount), Era);
        return delta.Money;
    }

    private int GetActionFlagCount(string prefix)
    {
        if (State is null) return 0;
        return State.FlagsSet.Count(f => f.StartsWith(prefix, StringComparison.Ordinal));
    }

    private void AddActionFlag(string prefix)
    {
        if (State is null) return;
        var count = GetActionFlagCount(prefix);
        State.SetFlag($"{prefix}{count + 1}");
    }

    private int GetSkillLevel(string skillKey)
    {
        if (State is null) return 0;
        var prefix = $"skill_{skillKey}_";
        var flag = State.FlagsSet.FirstOrDefault(f => f.StartsWith(prefix, StringComparison.Ordinal));
        if (flag is null) return 0;
        return int.TryParse(flag.AsSpan(prefix.Length), out var level) ? level : 0;
    }

    private int IncrementSkillLevel(string skillKey)
    {
        if (State is null) return 0;
        var prefix = $"skill_{skillKey}_";
        var current = GetSkillLevel(skillKey);
        var flag = State.FlagsSet.FirstOrDefault(f => f.StartsWith(prefix, StringComparison.Ordinal));
        if (flag is not null)
        {
            State.FlagsSet.Remove(flag);
        }

        var next = current + 1;
        State.SetFlag($"{prefix}{next}");
        return next;
    }

    private static string SkillTierLabel(int practiceCount) => practiceCount switch
    {
        >= 30 => "宗師級 Master",
        >= 15 => "熟練 Skilled",
        >= 5 => "入門 Beginner",
        _ => "新手 Novice"
    };

    public async Task<string> StudyHardAsync()
    {
        if (State is null) return "無效狀態";
        if (State.HasFlag("action_studied")) return "今年已經努力溫過書啦！";

        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Education: 5, Stress: 3));
        State.SetFlag("action_studied");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return "你通宵溫書，學識增加咗，但壓力都大咗。";
    }

    public async Task<string> AttendTutoringAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_tutor_") >= 2) return "今年補習補得夠多啦，消化下先！";

        var cost = ScaleMoney(-2000);
        if (State.Stats.Money < Math.Abs(cost)) return "唔夠錢報名補習班！";

        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, Education: 10, Stress: 1));
        AddActionFlag("action_tutor_");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"你報讀咗名師補習班，操練咗好多題目！(花費 ${Math.Abs(cost):N0})";
    }

    // --- Career tracks: turns education/DSE stream investment into an actual salary path ---

    private static readonly Dictionary<string, (string Name, int EduRequirement, int PayMin, int PayMax)> CareerTrackInfo = new(StringComparer.Ordinal)
    {
        ["factory"] = ("廠房工人 Factory Worker", 0, 1500, 2500),
        ["trading"] = ("貿易行 Trading", 30, 2500, 4000),
        ["fishing"] = ("漁民 Fisherman", 0, 1500, 2500),
        ["tailor"] = ("裁縫 Tailor", 20, 2000, 3500),
        ["manufacturing"] = ("製造業 Manufacturing", 20, 2500, 4000),
        ["finance"] = ("金融業 Finance", 60, 5000, 9000),
        ["civil_service"] = ("公務員 Civil Service", 50, 4000, 7000),
        ["retail"] = ("零售業 Retail", 0, 1500, 2500),
        ["tech"] = ("科技業 Tech", 55, 5000, 9000),
        ["logistics"] = ("物流業 Logistics", 20, 2500, 4000),
        ["creative"] = ("創意行業 Creative", 40, 3000, 5500),
        ["gig"] = ("自由工作者 Freelancer", 0, 2000, 3500),
    };

    public IReadOnlyList<(string Id, string Name, int EduRequirement, bool Eligible)> GetAvailableCareerTracks()
    {
        if (State is null || Era is null) return [];

        return [.. Era.AvailableCareerTracks
            .Where(CareerTrackInfo.ContainsKey)
            .Select(id =>
            {
                var info = CareerTrackInfo[id];
                return (Id: id, Name: info.Name, EduRequirement: info.EduRequirement, Eligible: State.Stats.Education >= info.EduRequirement);
            })];
    }

    public string? GetChosenCareerTrack()
    {
        if (State is null) return null;
        var flag = State.FlagsSet.FirstOrDefault(f => f.StartsWith("career_track_", StringComparison.Ordinal));
        return flag?["career_track_".Length..];
    }

    public async Task<string> ChooseCareerAsync(string trackId)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        if (State is null || Era is null) return "無效狀態";
        if (State.Age < 18) return "你未夠18歲，未夠班出嚟做嘢！";
        if (!CareerTrackInfo.TryGetValue(trackId, out var info)) return "呢一行喺呢個年代未有出現！";
        if (State.Stats.Education < info.EduRequirement) return $"你嘅學歷未夠（需要學歷 ≥ {info.EduRequirement}），未夠班入行！";

        var current = GetChosenCareerTrack();
        var before = State.Stats;
        if (current is not null)
        {
            State.FlagsSet.Remove($"career_track_{current}");
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Stress: 5));
        }

        State.SetFlag($"career_track_{trackId}");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return current is null
            ? $"🎉 你正式入行做「{info.Name}」，展開你嘅職業生涯！"
            : $"🔄 你轉咗行，而家做緊「{info.Name}」。轉工總要適應一下。";
    }

    public int GetCareerLevel()
    {
        var track = GetChosenCareerTrack();
        return track is null ? 0 : GetSkillLevel($"career_{track}");
    }

    public static string CareerRankLabel(int level) => level switch
    {
        >= 20 => "總監/合伙人 Director",
        >= 10 => "經理 Manager",
        >= 5 => "高級 Senior",
        _ => "初級 Junior"
    };

    public async Task<string> GoToCareerJobAsync()
    {
        if (State is null) return "無效狀態";
        var trackId = GetChosenCareerTrack();
        if (trackId is null || !CareerTrackInfo.TryGetValue(trackId, out var info)) return "你未選擇職業！";
        if (State.HasFlag("action_career_work")) return "今年已經返過工啦！";

        var before = State.Stats;
        State.SetFlag("action_career_work");
        var level = IncrementSkillLevel($"career_{trackId}");
        var rank = CareerRankLabel(level);
        var basePay = Random.Shared.Next(info.PayMin, info.PayMax + 1);
        var levelBonus = level * 120;
        var pay = ScaleMoney(basePay + levelBonus);

        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: pay, Reputation: 3, Stress: 5));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();

        var promoted = level is 5 or 10 or 20;
        return promoted
            ? $"🎊 你獲得晉升，而家係「{info.Name}」嘅{rank}！人工都加咗唔少。(獲得 ${pay:N0})"
            : $"💼 你返緊「{info.Name}」({rank})，努力工作為生活打拼。(獲得 ${pay:N0})";
    }

    public async Task<string> WorkHardAsync()
    {
        if (State is null) return "無效狀態";
        if (State.HasFlag("action_worked")) return "今年已經OT過，注意身體！";

        var before = State.Stats;
        var bonus = ScaleMoney(Random.Shared.Next(800, 2600));
        var bigBonus = Random.Shared.Next(100) < 10;
        if (bigBonus)
        {
            bonus = ScaleMoney(4000);
        }
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: bonus, Reputation: 3, Stress: 4));
        State.SetFlag("action_worked");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return bigBonus
            ? $"🌟 你嘅表現令老細喜出望外，仲俾埋大花紅！(獲得 ${bonus:N0})"
            : $"你OT到深夜，老細對你讚不絕口，仲發咗少少獎金！(獲得 ${bonus:N0})";
    }

    public async Task<string> GigWorkAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_gig_") >= 3) return "今年做咗好多兼職，太攰啦！";

        var pay = ScaleMoney(Random.Shared.Next(600, 1500));
        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: pay, Stress: 4, Health: -2));
        AddActionFlag("action_gig_");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"你去做炒散外賣，賺到啲辛苦錢。(獲得 ${pay:N0})";
    }

    public async Task<string> SpendFamilyTimeAsync()
    {
        if (State is null) return "無效狀態";
        if (State.HasFlag("action_family_chat")) return "今年已經陪過家人啦。";

        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(FamilyBond: 8, Stress: -3));
        State.SetFlag("action_family_chat");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return "你同屋企人一齊食飯傾偈，感覺好溫馨。";
    }

    public async Task<string> BuyFamilyGiftAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_family_gift_") >= 2) return "送禮物心意夠就得，唔使買咁多！";

        var cost = ScaleMoney(-1000);
        if (State.Stats.Money < Math.Abs(cost)) return "唔夠錢買禮物！";

        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, FamilyBond: 15, Stress: -2));
        AddActionFlag("action_family_gift_");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"你買咗份禮物送俾屋企人，大家都好開心！(花費 ${Math.Abs(cost):N0})";
    }

    public async Task<string> FindPartnerAsync()
    {
        if (State is null) return "無效狀態";
        if (State.HasFlag("has_partner")) return "你已經有伴侶啦，專一啲！";
        if (GetActionFlagCount("action_partner_find_") >= 2) return "緣份唔可以強求，等下年再試啦！";

        AddActionFlag("action_partner_find_");
        var success = Random.Shared.Next(100) < 50;
        if (success)
        {
            State.SetFlag("has_partner");
            var name = NpcNames[Random.Shared.Next(NpcNames.Length)];
            var trait = NpcTraits[Random.Shared.Next(NpcTraits.Length)];
            State.SetFlag($"partner_{name}_{trait}");
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"你鼓起勇氣向{name}表白，對方紅住臉應承咗——你成功出Pool啦！";
        }
        else
        {
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return "你嘗試向心儀對象表白，可惜對方話暫時想專注讀書/工作，請你食咗檸檬。";
        }
    }

    public (string Name, string Trait)? GetPartnerInfo()
    {
        if (State is null) return null;
        var flag = State.FlagsSet.FirstOrDefault(f => f.StartsWith("partner_", StringComparison.Ordinal));
        if (flag is null) return null;

        var rest = flag["partner_".Length..];
        var idx = rest.LastIndexOf('_');
        return idx < 0 ? (rest, string.Empty) : (rest[..idx], rest[(idx + 1)..]);
    }

    public async Task<string> MarryPartnerAsync()
    {
        if (State is null) return "無效狀態";
        if (!State.HasFlag("has_partner")) return "你仲未有伴侶！";
        if (State.HasFlag("married")) return "你已經結咗婚啦！";

        var cost = ScaleMoney(-8000);
        if (State.Stats.Money < Math.Abs(cost)) return "擺酒嘅錢都未儲夠，遲啲先啦！";

        var before = State.Stats;
        State.SetFlag("married");
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, Stress: 10, FamilyBond: 25, Reputation: 5));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        var partner = GetPartnerInfo();
        return $"💍 你同{partner?.Name ?? "另一半"}擺酒結婚，親朋戚友都嚟慶祝，人生一大喜事！(花費 ${Math.Abs(cost):N0})";
    }

    public async Task<string> DivorceAsync()
    {
        if (State is null) return "無效狀態";
        if (!State.HasFlag("married")) return "你未結婚，邊有得離婚！";

        var cost = ScaleMoney(-5000);
        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, Stress: 15, FamilyBond: -20, Reputation: -5));
        State.FlagsSet.Remove("married");
        State.FlagsSet.Remove("has_partner");
        var partnerFlag = State.FlagsSet.FirstOrDefault(f => f.StartsWith("partner_", StringComparison.Ordinal));
        if (partnerFlag is not null)
        {
            State.FlagsSet.Remove(partnerFlag);
        }

        State.SetFlag("divorced");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"💔 你哋感情已經破裂，決定簽紙離婚，各自展開新生活。(花費 ${Math.Abs(cost):N0})";
    }

    // --- Parenting ---

    public IReadOnlyList<(string Key, string Name)> GetChildren()
    {
        if (State is null) return [];

        return [.. State.FlagsSet
            .Where(f => f.StartsWith("child_", StringComparison.Ordinal))
            .Select(f => f["child_".Length..])
            .Select(rest =>
            {
                var idx = rest.IndexOf('_', StringComparison.Ordinal);
                return idx < 0 ? (Key: rest, Name: rest) : (Key: rest, Name: rest[(idx + 1)..]);
            })];
    }

    public async Task<string> HaveBabyAsync()
    {
        if (State is null) return "無效狀態";
        if (!State.HasFlag("married")) return "你未結婚，未可以要小朋友！";

        var childCount = GetChildren().Count;
        if (childCount >= 3) return "你哋已經有三個小朋友，家庭都幾熱鬧㗎啦！";
        if (State.HasFlag("action_have_baby")) return "今年已經有咗好消息，慢慢嚟先！";

        var cost = ScaleMoney(-10000);
        if (State.Stats.Money < Math.Abs(cost)) return "養育小朋友使費唔少，你哋而家未夠錢！";

        var before = State.Stats;
        State.SetFlag("action_have_baby");
        var name = NpcNames[Random.Shared.Next(NpcNames.Length)];
        var childIndex = childCount + 1;
        State.SetFlag($"child_{childIndex}_{name}");
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, Stress: 12, FamilyBond: 20, Health: -3));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"👶 恭喜！你哋迎接咗新成員 {name} 嚟到呢個家庭，一家人開心到喊！(花費 ${Math.Abs(cost):N0})";
    }

    public async Task<string> SpendTimeWithChildAsync(string childKey)
    {
        ArgumentNullException.ThrowIfNull(childKey);
        if (State is null) return "無效狀態";

        var hasChild = State.FlagsSet.Any(f => f.StartsWith($"child_{childKey}", StringComparison.Ordinal));
        if (!hasChild) return "搵唔到呢個小朋友！";

        var prefix = $"action_parenting_{childKey}_";
        if (GetActionFlagCount(prefix) >= 2) return "今年已經同呢個小朋友玩夠喇，等下次先！";

        var cost = ScaleMoney(-200);
        var before = State.Stats;
        AddActionFlag(prefix);

        var name = childKey.Contains('_', StringComparison.Ordinal) ? childKey[(childKey.IndexOf('_', StringComparison.Ordinal) + 1)..] : childKey;
        var roll = Random.Shared.Next(100);

        if (roll < 15)
        {
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, FamilyBond: 10, Stress: -6, Education: 2));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"📚 你陪{name}溫書做功課，仲教識咗佢新嘢，佢好開心！(花費 ${Math.Abs(cost):N0})";
        }

        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, FamilyBond: 7, Stress: -3));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"🎡 你帶{name}去公園玩，佢成日笑，你都覺得好幸福。(花費 ${Math.Abs(cost):N0})";
    }

    public async Task<string> DatePartnerAsync()
    {
        if (State is null) return "無效狀態";
        if (!State.HasFlag("has_partner")) return "你仲未有伴侶！";
        if (GetActionFlagCount("action_partner_date_") >= 2) return "拍拖雖然好，但都要留返時間俾自己！";

        var cost = ScaleMoney(-800);
        if (State.Stats.Money < Math.Abs(cost)) return "唔夠錢去拍拖！";

        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, FamilyBond: 6, Stress: -5));
        AddActionFlag("action_partner_date_");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"你同伴侶去行街睇戲食飯，甜甜蜜蜜！(花費 ${Math.Abs(cost):N0})";
    }

    public async Task<string> BuyLotteryAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_lottery_") >= 5) return "投注要適可而止，今年買夠啦！";

        var cost = -20;
        if (State.Stats.Money < Math.Abs(cost)) return "唔夠錢買六合彩！";

        var before = State.Stats;
        AddActionFlag("action_lottery_");

        var roll = Random.Shared.Next(1000);
        if (roll == 8) // 1 in 1000 jackpot
        {
            var jackpot = ScaleMoney(5000000);
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost + jackpot, Reputation: 10, Stress: -5));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"🎯 恭喜你！中咗六合彩頭獎！贏得獎金 ${jackpot:N0}！成為百萬富翁！";
        }
        else if (roll < 10) // 1 in 100 small prize (9 matching numbers)
        {
            var prize = ScaleMoney(10000);
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost + prize, Stress: -2));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"🎉 唔錯喎！中咗六合彩小獎，贏得獎金 ${prize:N0}！";
        }
        else
        {
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return "好遺憾，你張六合彩無中獎。下期再接再厲！";
        }
    }

    public async Task<string> GoGymAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_gym_") >= 2) return "健身都要適量，操得太頻繁會受傷！";

        var cost = ScaleMoney(-200);
        if (State.Stats.Money < Math.Abs(cost)) return "唔夠錢做Gym！";

        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, Health: 6, Stress: -3));
        AddActionFlag("action_gym_");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"你去咗做健身，跑下步舉下鐵，出身汗個人輕鬆晒！(花費 ${Math.Abs(cost):N0})";
    }

    public async Task<string> VisitDoctorAsync()
    {
        if (State is null) return "無效狀態";

        var cost = ScaleMoney(-800);
        if (State.Stats.Money < Math.Abs(cost)) return "唔夠錢睇醫生！";

        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, Health: 15, Stress: -2));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"你去睇咗家庭醫生，醫生開咗藥俾你，叮囑你多啲休息。(花費 ${Math.Abs(cost):N0})";
    }

    public async Task<string> EmigrateAsync(string destination)
    {
        if (State is null) return "無效狀態";
        if (State.HasFlag("emigrated")) return "你已經移民咗去外國啦！";

        var cost = ScaleMoney(-50000);
        if (State.Stats.Money < Math.Abs(cost)) return $"唔夠錢移民！需要至少 ${Math.Abs(cost):N0}";

        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, Stress: 8, Reputation: 5));
        State.SetFlag("emigrated");
        State.SetFlag($"emigrated_{destination.ToUpperInvariant()}");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();

        var destName = destination switch
        {
            "UK" => "英國",
            "Australia" => "澳洲",
            "Canada" => "加拿大",
            "US" => "美國",
            _ => destination
        };

        return $"你簽好晒文件，執拾行李飛往 {destName}。開始你嘅海外移民生活！(花費 ${Math.Abs(cost):N0})";
    }

    public async Task<string> BuyPropertyAsync(string tier)
    {
        if (State is null || Era is null) return "無效狀態";
        if (State.HasFlag("homeowner")) return "你已經有一層樓啦，請先賣出舊樓以換新樓！";

        var priceMultiplier = tier switch
        {
            "tonglau" => 0.5m,
            "private" => 1.0m,
            "luxury" => 2.5m,
            _ => 1.0m
        };

        var price = (int)Math.Round(Era.AverageHousePrice * priceMultiplier);
        if (State.Stats.Money < price) return $"你唔夠錢買呢層樓！需要 ${price:N0}";

        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: -price));
        State.SetFlag("homeowner");
        State.SetFlag($"home_{tier}");

        var name = tier switch
        {
            "tonglau" => "舊區唐樓單位",
            "private" => "市區私人屋苑",
            "luxury" => "半山豪宅別墅",
            _ => "住宅物業"
        };

        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"恭喜上車！你買入咗 {name}，正式成為業主！(花費 ${price:N0})";
    }

    public async Task<string> SellPropertyAsync(string tier)
    {
        if (State is null || Era is null) return "無效狀態";
        if (!State.HasFlag($"home_{tier}")) return "你並未持有呢種類型嘅物業！";

        var priceMultiplier = tier switch
        {
            "tonglau" => 0.5m,
            "private" => 1.0m,
            "luxury" => 2.5m,
            _ => 1.0m
        };

        var basePrice = (int)Math.Round(Era.AverageHousePrice * priceMultiplier);

        // Add some random market fluctuation (+/- 10%)
        var variancePct = (decimal)(Random.Shared.NextDouble() * 0.2 - 0.1); // -0.10 to +0.10
        var finalPrice = (int)Math.Round(basePrice * (1.0m + variancePct));

        var before = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: finalPrice));
        State.FlagsSet.Remove("homeowner");
        State.FlagsSet.Remove($"home_{tier}");

        var name = tier switch
        {
            "tonglau" => "舊區唐樓單位",
            "private" => "市區私人屋苑",
            "luxury" => "半山豪宅別墅",
            _ => "住宅物業"
        };

        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"你成功出售咗 {name}，市場交易價為 ${finalPrice:N0}！(增幅/跌幅: {variancePct * 100:F1}%)";
    }

    // --- Hobbies ---

    public async Task<string> PracticeMusicAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_music_") >= 2) return "今年已經夾咗好多次Band，休息下把口先！";

        var cost = ScaleMoney(-300);
        if (State.Stats.Money < Math.Abs(cost)) return "唔夠錢交音樂堂學費！";

        var before = State.Stats;
        AddActionFlag("action_music_");
        var level = IncrementSkillLevel("music");
        var tier = SkillTierLabel(level);
        var buskingChance = Math.Min(20 + level, 60);

        var busking = Random.Shared.Next(100) < buskingChance;
        if (busking)
        {
            var tips = ScaleMoney(400 + (level * 30));
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost + tips, Reputation: 4, Stress: -6));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"🎸 你去街頭表演，一班街坊圍住聽，仲有人打賞！(淨收 ${tips:N0}，音樂技能：{tier})";
        }

        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, Reputation: 2, Stress: -6));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"🎹 你上咗堂音樂課，練習吉他/鋼琴，好療癒！(花費 ${Math.Abs(cost):N0}，音樂技能：{tier})";
    }

    public async Task<string> PlaySportsAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_sports_") >= 2) return "今年波都踢/打得夠多啦，小心受傷！";

        var before = State.Stats;
        AddActionFlag("action_sports_");
        var level = IncrementSkillLevel("sports");
        var tier = SkillTierLabel(level);
        var injuryChance = Math.Max(10 - (level / 3), 2);

        var injury = Random.Shared.Next(100) < injuryChance;
        if (injury)
        {
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Health: -4, Stress: -3, FamilyBond: 4));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"⚽ 你同波友踢咗場波，唔小心拗柴受咗少少傷，不過同班兄弟感情更好！(運動技能：{tier})";
        }

        var healthGain = 5 + (level / 5);
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Health: healthGain, Stress: -6, FamilyBond: 3));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"🏀 你同班波友打波/踢波，流曬汗，心情爽晒！(運動技能：{tier})";
    }

    public bool IsMusicMaster() => GetSkillLevel("music") >= 30;

    public bool IsSportsMaster() => GetSkillLevel("sports") >= 30;

    public async Task<string> ProfessionalMusicianAsync()
    {
        if (State is null) return "無效狀態";
        if (!IsMusicMaster()) return "你嘅音樂技能未到宗師級，未夠班出道！";
        if (State.HasFlag("action_pro_musician")) return "今年已經接過音樂演出啦！";

        var before = State.Stats;
        State.SetFlag("action_pro_musician");
        State.SetFlag("career_musician");
        var pay = ScaleMoney(Random.Shared.Next(6000, 12000));
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: pay, Reputation: 8, Stress: 4));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"🎤 憑住宗師級音樂技能，你獲邀喺場地正式演出，成為職業音樂人！(獲得 ${pay:N0})";
    }

    public async Task<string> ProfessionalAthleteAsync()
    {
        if (State is null) return "無效狀態";
        if (!IsSportsMaster()) return "你嘅運動技能未到宗師級，未夠班入行！";
        if (State.HasFlag("action_pro_athlete")) return "今年已經比賽過啦，休息下先！";

        var before = State.Stats;
        State.SetFlag("action_pro_athlete");
        State.SetFlag("career_athlete");
        var pay = ScaleMoney(Random.Shared.Next(6000, 12000));
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: pay, Reputation: 8, Health: -4, Stress: 6));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"🏆 憑住宗師級運動技能，你代表香港出賽並奪得獎金，成為職業運動員！(獲得 ${pay:N0})";
    }

    public async Task<string> ReadBooksAsync()
    {
        if (State is null) return "無效狀態";
        if (State.HasFlag("action_reading")) return "今年已經睇夠書啦，眼訓喇！";

        var before = State.Stats;
        State.SetFlag("action_reading");
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Education: 2, Stress: -4, Money: ScaleMoney(-50)));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return "📖 你去咗圖書館/租書舖，靜靜地睇咗幾本書，心情平靜返好多。";
    }

    public async Task<string> PlayVideoGamesAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_gaming_") >= 2) return "今年打機打得夠多啦，眼訓喇！";

        var before = State.Stats;
        AddActionFlag("action_gaming_");

        var tournamentWin = Random.Shared.Next(100) < 10;
        if (tournamentWin)
        {
            var prize = ScaleMoney(2000);
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: prize, Reputation: 5, Stress: -4));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"🏆 你參加咗網上電競比賽，估唔到贏咗獎金！(獲得 ${prize:N0})";
        }

        State.Stats = State.Stats.ApplyDelta(new StatDelta(Health: -2, Stress: -7, Reputation: 1));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return "🎮 你打咗成晚機，同網友一齊開黑，好紓壓！(不過對眼有啲累)";
    }

    public async Task<string> CookingAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_cooking_") >= 2) return "今年煮咗好多次飯啦，等下次先！";

        var cost = ScaleMoney(-150);
        if (State.Stats.Money < Math.Abs(cost)) return "唔夠錢買餸！";

        var before = State.Stats;
        AddActionFlag("action_cooking_");

        var burnt = Random.Shared.Next(100) < 15;
        if (burnt)
        {
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, Stress: 2, Health: -1));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"🔥 你買咗餸想露一手，點知整燶咗，仲要叫外賣！(花費 ${Math.Abs(cost):N0})";
        }

        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: cost, FamilyBond: 5, Health: 2, Stress: -3));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"🍳 你落廚煮咗一餐俾屋企人食，大家讚不絕口！(花費 ${Math.Abs(cost):N0})";
    }

    // --- Part-time job variety ---

    public async Task<string> TutorPartTimeJobAsync()
    {
        if (State is null) return "無效狀態";
        if (State.Stats.Education < 40) return "你嘅學歷未夠格做補習老師！(需要學歷 >= 40)";
        if (GetActionFlagCount("action_tutorjob_") >= 2) return "今年補習堂教得夠多啦，唔好累壞學生！";

        var before = State.Stats;
        AddActionFlag("action_tutorjob_");
        var pay = ScaleMoney(Random.Shared.Next(1200, 2200));
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: pay, Reputation: 4, Stress: 3));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"📐 你兼職做補習老師，教學生做功課溫書，人工唔錯！(獲得 ${pay:N0})";
    }

    public async Task<string> RetailPartTimeJobAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_retailjob_") >= 3) return "今年做咗好多更零售兼職，攰啦！";

        var before = State.Stats;
        AddActionFlag("action_retailjob_");
        var pay = ScaleMoney(Random.Shared.Next(500, 900));
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: pay, Reputation: 1, Stress: 2, Health: -1));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"🛍️ 你喺商場兼職做售貨員，企足成日，賺到少少人工。(獲得 ${pay:N0})";
    }

    public async Task<string> HandoutFlyersJobAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_flyersjob_") >= 3) return "今年派咗好多次傳單啦，休息下先！";

        var before = State.Stats;
        AddActionFlag("action_flyersjob_");
        var pay = ScaleMoney(Random.Shared.Next(300, 600));
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: pay, Stress: 2, Health: -1));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"📄 你喺街口企咗成日派傳單，人工雖少但幾易做。(獲得 ${pay:N0})";
    }

    public async Task<string> LivestreamJobAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_livestream_") >= 2) return "今年直播咗好多次啦，畀啲時間休息下把聲！";

        var before = State.Stats;
        AddActionFlag("action_livestream_");
        var roll = Random.Shared.Next(100);

        if (roll < 15)
        {
            var viral = ScaleMoney(5000);
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: viral, Reputation: 8, Stress: 3));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"🔥 你嘅直播意外爆紅，訂閱人數暴增，仲收到打賞！(獲得 ${viral:N0})";
        }

        if (roll < 45)
        {
            var pay = ScaleMoney(800);
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: pay, Reputation: 2, Stress: 2));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"📹 你開咗場直播分享日常，反應都算唔錯。(獲得 ${pay:N0})";
        }

        State.Stats = State.Stats.ApplyDelta(new StatDelta(Reputation: -1, Stress: 3));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return "😅 你開咗場直播，不過冇乜人睇，仲要畀人留言鬧，有啲灰心。";
    }

    // --- NPC friends (procedurally generated, not requiring a real player's share code) ---

    private static readonly string[] NpcNames =
        ["阿明", "阿珍", "家豪", "淑芬", "志偉", "美玲", "俊傑", "心怡", "國強", "麗華", "偉倫", "佩珊", "子健", "詠詩", "永權"];

    private static readonly string[] NpcTraits =
        ["開朗", "搞笑", "老實", "型格", "醒目", "熱心", "文靜", "好動"];

    public IReadOnlyList<(string Key, string Name, string Trait)> GetNpcFriends()
    {
        if (State is null) return [];

        return [.. State.FlagsSet
            .Where(f => f.StartsWith("npc_friend_", StringComparison.Ordinal))
            .Select(f => f["npc_friend_".Length..])
            .Select(rest =>
            {
                var idx = rest.LastIndexOf('_');
                return idx < 0 ? (Key: rest, Name: rest, Trait: string.Empty) : (Key: rest, Name: rest[..idx], Trait: rest[(idx + 1)..]);
            })];
    }

    public async Task<string> MakeNpcFriendAsync()
    {
        if (State is null) return "無效狀態";
        if (State.HasFlag("action_make_friend")) return "今年已經識咗新朋友啦，慢慢培養感情先！";

        State.SetFlag("action_make_friend");

        var existing = GetNpcFriends().Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var candidates = (from name in NpcNames
                           from trait in NpcTraits
                           select $"{name}_{trait}").Where(key => !existing.Contains(key)).ToList();

        if (candidates.Count == 0)
        {
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return "你身邊嘅朋友圈已經好熱鬧，暫時冇新朋友加入！";
        }

        var success = Random.Shared.Next(100) < 60;
        if (!success)
        {
            var before = State.Stats;
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Stress: 1));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return "你試住同班同學/同事搭訕，不過大家都幾忙，未夾到時間傾偈。";
        }

        var pick = candidates[Random.Shared.Next(candidates.Count)];
        State.SetFlag($"npc_friend_{pick}");
        State.SetFlag("has_npc_friend");
        var parts = pick.Split('_');

        var beforeStats = State.Stats;
        State.Stats = State.Stats.ApplyDelta(new StatDelta(FamilyBond: 3, Stress: -2));
        StatDeltaToast = BuildDeltaToast(beforeStats, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"🎉 你識到咗一個新朋友：{parts[0]}（性格：{parts[1]}），大家傾得幾投緣！";
    }

    public int GetFriendshipLevel(string npcKey) => GetSkillLevel($"npcbond_{npcKey}");

    public static string FriendshipTierLabel(int level) => level switch
    {
        >= 6 => "死黨 Best Friend",
        >= 3 => "好友 Friend",
        _ => "相識 Acquaintance"
    };

    public async Task<string> HangOutWithNpcAsync(string npcKey)
    {
        ArgumentNullException.ThrowIfNull(npcKey);
        if (State is null) return "無效狀態";
        if (!State.HasFlag($"npc_friend_{npcKey}")) return "你同呢位仲未係朋友！";

        var hangoutPrefix = $"action_hangout_{npcKey}_";
        if (GetActionFlagCount(hangoutPrefix) >= 2) return "今年同呢位朋友已經玩夠喇，返去搵下其他人啦！";

        var before = State.Stats;
        AddActionFlag(hangoutPrefix);
        var level = IncrementSkillLevel($"npcbond_{npcKey}");
        var tier = FriendshipTierLabel(level);

        var name = npcKey.Split('_')[0];
        var roll = Random.Shared.Next(100);

        if (roll < 15)
        {
            var gift = ScaleMoney(300);
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: gift, FamilyBond: 6, Stress: -5));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"🎁 你同{name}出街食飯傾偈，佢仲請埋你食飯！(獲得 ${gift:N0}，friendship：{tier})";
        }

        if (roll < 25)
        {
            var loan = ScaleMoney(-500);
            if (State.Stats.Money >= Math.Abs(loan))
            {
                State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: loan, FamilyBond: 8, Stress: -3));
                StatDeltaToast = BuildDeltaToast(before, State.Stats);
                await AutosaveAsync().ConfigureAwait(false);
                Changed?.Invoke();
                return $"🥲 {name}話手緊，你夠義氣借咗少少錢俾佢，友誼更加深厚。(花費 ${Math.Abs(loan):N0}，friendship：{tier})";
            }
        }

        State.Stats = State.Stats.ApplyDelta(new StatDelta(FamilyBond: 6, Stress: -4));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"😊 你同{name}一齊食飯睇戲傾心事，放鬆咗好多。(friendship：{tier})";
    }

    public async Task<string> AskFriendForFavorAsync(string npcKey)
    {
        ArgumentNullException.ThrowIfNull(npcKey);
        if (State is null) return "無效狀態";
        if (!State.HasFlag($"npc_friend_{npcKey}")) return "你同呢位仲未係朋友！";
        if (GetFriendshipLevel(npcKey) < 6) return "你哋交情未夠深，未係開口問呢啲嘢嘅時候。";
        if (State.HasFlag($"action_favor_{npcKey}")) return "今年已經問過佢幫手啦，唔好搞到段友誼太緊張！";

        State.SetFlag($"action_favor_{npcKey}");
        var name = npcKey.Split('_')[0];
        var before = State.Stats;

        var roll = Random.Shared.Next(100);
        if (roll < 60)
        {
            var bonus = ScaleMoney(3000);
            State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: bonus, Reputation: 5, FamilyBond: 3));
            StatDeltaToast = BuildDeltaToast(before, State.Stats);
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return $"💼 死黨{name}喺公司幫你搭路，介紹咗一單筍工/大生意俾你！(獲得 ${bonus:N0})";
        }

        var emergencyLoan = ScaleMoney(5000);
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: emergencyLoan, Stress: -8, FamilyBond: 5));
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"🤝 你手緊嗰陣，死黨{name}二話不說借咗一大筆錢俾你應急，真係過命交情！(獲得 ${emergencyLoan:N0})";
    }

    // --- Random HK-flavored world news, rolled once per year during AdvanceYearAsync ---

    public string? WorldNewsMessage { get; private set; }

    private void RollWorldNewsEvent()
    {
        WorldNewsMessage = null;
        if (State is null || Random.Shared.Next(100) >= 35)
        {
            return;
        }

        (string Message, StatDelta Delta)[] pool =
        [
            ("📉 環球股市大跌，市場人心惶惶。", new StatDelta(Money: ScaleMoney(-200), Stress: 2)),
            ("📈 政府公布經濟支援措施，市面消費氣氛回暖。", new StatDelta(Money: ScaleMoney(200), Stress: -1)),
            ("🌪️ 十號風球襲港，全城掛波，你放咗一日假。", new StatDelta(Stress: -3)),
            ("☔ 黑色暴雨警告生效，返工放工都幾狼狽。", new StatDelta(Stress: 2, Health: -1)),
            ("💻 科技行業掀起新一輪裁員潮，你身邊有朋友受影響。", new StatDelta(Stress: 2)),
            ("🏠 樓市出現反彈，業主們笑逐顏開。", new StatDelta(Reputation: 1)),
            ("💰 銀行宣布加息，供樓一族百上加斤。", new StatDelta(Stress: 2)),
            ("🦠 流感高峰期殺到，記得小心身體。", new StatDelta(Health: -2)),
            ("🎉 香港代表隊喺國際賽事贏得獎牌，全城歡呼！", new StatDelta(Stress: -2, FamilyBond: 1)),
            ("🎓 政府推出獎學金計劃，莘莘學子受惠。", new StatDelta(Education: 1)),
            ("🚇 鐵路服務大改善，通勤時間縮短咗，心情都好啲。", new StatDelta(Stress: -1)),
            ("📱 新款手機發布，全城掀起搶購潮。", new StatDelta(Reputation: 1)),
            ("🧧 農曆新年將至，親戚朋友派利是，你袋袋平安。", new StatDelta(Money: ScaleMoney(300), FamilyBond: 2)),
            ("🪙 加密貨幣市場暴跌，唔少街坊蝕入肉。", new StatDelta(Stress: 2)),
            ("🎆 除夕維港煙花匯演，全城歡度佳節。", new StatDelta(Stress: -2, FamilyBond: 1)),
            ("⚡ 突然停電，全城陷入一片混亂，公司仲要提早收工。", new StatDelta(Stress: 1)),
            ("🏗️ 市區重建計劃展開，樓價再創新高。", new StatDelta(Reputation: 1)),
            ("🚕 的士車隊集體加價，市民出行成本上升。", new StatDelta(Money: ScaleMoney(-100))),
            ("🍜 米芝蓮指南新鮮出爐，本地餐廳揚威國際。", new StatDelta(Reputation: 1, Stress: -1)),
            ("🏥 公立醫院爆滿，等候時間創新高，你身邊有人受影響。", new StatDelta(Stress: 2)),
            ("🎬 香港電影喺國際影展攞獎，全城引以為榮。", new StatDelta(Reputation: 2, Stress: -1)),
            ("🐉 端午節龍舟競賽熱鬧舉行，你去咗睇熱鬧。", new StatDelta(Stress: -2)),
            ("📶 5G/6G網絡覆蓋擴展，上網速度快咗好多。", new StatDelta(Stress: -1)),
            ("🌊 天文台發出海嘯/風暴潮警告，沿海居民要小心。", new StatDelta(Stress: 2)),
            ("🎨 西九文化區新展覽開幕，藝文氣息濃厚。", new StatDelta(Reputation: 1, Education: 1)),
            ("💼 大型企業裁員消息傳出，就業市場氣氛緊張。", new StatDelta(Stress: 2)),
            ("🏃 香港馬拉松盛大舉行，全城運動氣氛高漲。", new StatDelta(Health: 1, Stress: -1)),
            ("🐷 豬肉/蔬菜價格飆升，主婦們叫苦連天。", new StatDelta(Money: ScaleMoney(-150))),
            ("🎇 維港跨年倒數活動吸引萬人參與，氣氛熱烈。", new StatDelta(Stress: -2)),
            ("🧬 本地大學研發新科技獲國際認可，港人揚眉吐氣。", new StatDelta(Reputation: 2, Education: 1)),
        ];

        var (message, delta) = pool[Random.Shared.Next(pool.Length)];
        State.Stats = State.Stats.ApplyDelta(delta);
        WorldNewsMessage = message;
    }

    private async Task AutosaveAsync()
    {
        if (State is null || Chain is null)
        {
            return;
        }

        try
        {
            await _saveManager.SaveAsync(State, AutosaveSlot, Chain.Lineage).ConfigureAwait(false);
            SaveErrorMessage = null;
        }
        catch (JSException)
        {
            SaveErrorMessage = "儲存空間已滿";
        }
    }

    private void SetUpEngine()
    {
        if (Era is null || State is null)
        {
            return;
        }

        var events = _eventsByEra[Era.EraId];
        _engine = new EventEngine(events, Era, State.RngSeed);
        _lifecycle = new LifecycleSystem(State.RngSeed);
        
        // If loaded and event was already resolved this year, keep CurrentEvent null
        CurrentEvent = (State.IsAlive && !State.HasFlag("event_resolved_for_year")) ? _engine.SelectNextEvent(State) : null;
        StatDeltaToast = null;
        SaveErrorMessage = null;
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

