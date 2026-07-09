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

    public async Task<string> WorkHardAsync()
    {
        if (State is null) return "無效狀態";
        if (State.HasFlag("action_worked")) return "今年已經OT過，注意身體！";

        var before = State.Stats;
        var bonus = ScaleMoney(1500);
        State.Stats = State.Stats.ApplyDelta(new StatDelta(Money: bonus, Reputation: 3, Stress: 4));
        State.SetFlag("action_worked");
        StatDeltaToast = BuildDeltaToast(before, State.Stats);
        await AutosaveAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return $"你OT到深夜，老細對你讚不絕口，仲發咗少少獎金！(獲得 ${bonus:N0})";
    }

    public async Task<string> GigWorkAsync()
    {
        if (State is null) return "無效狀態";
        if (GetActionFlagCount("action_gig_") >= 3) return "今年做咗好多兼職，太攰啦！";

        var pay = ScaleMoney(1000);
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
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return "你鼓起勇氣表白，對方紅住臉應承咗——你成功出Pool啦！";
        }
        else
        {
            await AutosaveAsync().ConfigureAwait(false);
            Changed?.Invoke();
            return "你嘗試向心儀對象表白，可惜對方話暫時想專注讀書/工作，請你食咗檸檬。";
        }
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

