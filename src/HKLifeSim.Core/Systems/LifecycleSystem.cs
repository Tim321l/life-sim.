using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Systems;

public sealed class LifecycleSystem
{
    private const int AgingStartsAt = 50;
    private const int OldAgeRiskStartsAt = 85;
    private const int ForcedDeathAge = 100;

    private readonly Random _random;

    public LifecycleSystem(int seed)
    {
        _random = new Random(seed);
    }

    public string? AdvanceYear(GameState state, EraConfig era)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(era);

        state.Age++;
        state.CurrentYear++;
        state.Stats = state.Stats.ResetStamina();

        string? milestoneMsg = null;

        // Tuition deduction for poor families in older eras (1960s & 1980s)
        if (state.Age > 6 && state.Age <= 17 && state.HasFlag("family_poor") && (era.EraId == "1960s" || era.EraId == "1980s") && !state.HasFlag("dropped_out"))
        {
            var fee = era.EraId == "1960s" ? 50 : 150;
            if (state.Stats.Money < fee)
            {
                state.SetFlag("dropped_out");
                state.Stats = state.Stats.ApplyDelta(new StatDelta(Money: -state.Stats.Money));
                milestoneMsg = "💸 因家境貧寒，你無力支付學費而被逼退學！你唯有提早投身社會，去工廠做童工幫補家計。";
            }
            else
            {
                state.Stats = state.Stats.ApplyDelta(new StatDelta(Money: -fee));
                milestoneMsg = $"💸 繳納學費：家境清貧，父母辛辛苦苦為你湊齊學費 ${fee}。你發誓一定要努力讀書！";
            }
        }
        if (state.Age == 12 && !state.HasFlag("dropped_out"))
        {
            milestoneMsg = ExamSystem.AllocateSecondarySchool(state);
        }
        else if (state.Age == 17 && !state.HasFlag("dropped_out"))
        {
            milestoneMsg = ExamSystem.RunSchoolLeavingExam(state, _random, out var score);
            state.SetFlag($"exam_score_{score}");
        }
        else if (state.Age == 18 && era.EraId == "2024plus" && !state.HasFlag("dropped_out"))
        {
            var scoreFlag = state.FlagsSet.FirstOrDefault(f => f.StartsWith("exam_score_", StringComparison.Ordinal));
            var score = 15;
            if (scoreFlag != null && int.TryParse(scoreFlag.AsSpan(11), out var parsedScore))
            {
                score = parsedScore;
            }
            milestoneMsg = ExamSystem.RunUniversityAdmission(state, _random, score);
        }
        else if (state.Age == 19 && era.EraId != "2024plus" && !state.HasFlag("dropped_out"))
        {
            milestoneMsg = ExamSystem.RunUniversityAdmission(state, _random, 0);
        }
        else if (state.Age == 20 && state.HasFlag("college_student"))
        {
            if (state.Stats.Education >= 60)
            {
                state.FlagsSet.Remove("college_student");
                state.SetFlag("university_student");
                state.Stats = state.Stats.ApplyDelta(new StatDelta(Reputation: 5, Stress: 3));
                milestoneMsg = "🎓 銜接學位成功 (Non-JUPAS)：由於你在大專兩年努力讀書，學力達標，你成功銜接升讀本地大學學士學位三年級！";
            }
            else
            {
                state.FlagsSet.Remove("college_student");
                state.SetFlag("early_worker");
                state.Stats = state.Stats.ApplyDelta(new StatDelta(Money: 2000, Stress: -2));
                milestoneMsg = "💼 大專畢業：你完成了兩年副學位課程。由於學術成績未達銜接門檻，你正式畢業並投入社會工作。";
            }
        }

        if (state.Age >= AgingStartsAt)
        {
            state.Stats = state.Stats.ApplyDelta(new StatDelta(Stress: -1, Health: -1));
        }

        if (state.Stats.IsFatal(out var fatalCause))
        {
            state.IsAlive = false;
            state.DeathCause = fatalCause;
            return milestoneMsg;
        }

        if (state.Age >= ForcedDeathAge)
        {
            state.IsAlive = false;
            state.DeathCause = "old_age";
            return milestoneMsg;
        }

        if (state.Age >= OldAgeRiskStartsAt)
        {
            var deathChancePercent = (state.Age - (OldAgeRiskStartsAt - 1)) * 5;
            if (_random.Next(100) < deathChancePercent)
            {
                state.IsAlive = false;
                state.DeathCause = "old_age";
            }
        }

        return milestoneMsg;
    }
}
