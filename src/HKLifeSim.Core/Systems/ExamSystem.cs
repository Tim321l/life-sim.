using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Systems;

public static class ExamSystem
{
    public static string AllocateSecondarySchool(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var education = state.Stats.Education;
        string schoolName;
        string resultMsg;

        if (education >= 70)
        {
            state.SetFlag("school_band1");
            schoolName = state.EraId switch
            {
                "1960s" => "皇仁書院 (Queen's College)",
                "1980s" => "喇沙書院 (La Salle College)",
                "2000s" => "拔萃女書院 (Diocesan Girls' School)",
                _ => "聖保羅男女中學 (St. Paul's Co-educational College)"
            };
            state.Stats = state.Stats.ApplyDelta(new StatDelta(Reputation: 10, Stress: 5));
            resultMsg = $"🎯 恭喜！你以優異成績被獲派第一組別 (Band 1) 名校【{schoolName}】！聲望提升！";
        }
        else if (education >= 40)
        {
            state.SetFlag("school_band2");
            schoolName = "地區津貼中學 (Subsidized Secondary School)";
            state.Stats = state.Stats.ApplyDelta(new StatDelta(Reputation: 2));
            resultMsg = $"👍 派位結果：你獲派第二組別 (Band 2) 的【{schoolName}】。";
        }
        else
        {
            state.SetFlag("school_band3");
            schoolName = "實用職業中學 (Vocational Secondary School)";
            state.Stats = state.Stats.ApplyDelta(new StatDelta(Reputation: -5, Stress: 8));
            resultMsg = $"⚠️ 派位結果：你獲派第三組別 (Band 3) 的【{schoolName}】。要加把勁啦！";
        }

        return resultMsg;
    }

    public static string RunSchoolLeavingExam(GameState state, out int score)
    {
        ArgumentNullException.ThrowIfNull(state);

        var education = state.Stats.Education;
        var random = Random.Shared;

        if (state.EraId == "2024plus")
        {
            // HKDSE (6 subjects, max 42 points)
            int baseScore;
            if (education >= 80) baseScore = random.Next(30, 39); // 30-38 base
            else if (education >= 60) baseScore = random.Next(20, 30); // 20-29 base
            else if (education >= 40) baseScore = random.Next(12, 20); // 12-19 base
            else baseScore = random.Next(6, 12); // 6-11 base

            var bonus = state.HasFlag("exam_bonus_2") ? 4 : (state.HasFlag("exam_bonus_1") ? 2 : 0);
            score = Math.Min(42, baseScore + bonus);

            state.SetFlag("sat_dse");
            
            string levelMsg = score >= 30 ? "狀元級數 (Outstanding)" : (score >= 20 ? "良好 (Good)" : "一般 (Pass)");
            var dseDetails = $"【香港中學文憑試 HKDSE】你考獲最佳六科總分： {score} 分！ ({levelMsg}, 包含選修科答題加分: +{bonus}分)";
            return dseDetails;
        }
        else
        {
            // HKCEE (會考, max 30 points)
            int baseScore;
            if (education >= 80) baseScore = random.Next(20, 28);
            else if (education >= 60) baseScore = random.Next(14, 20);
            else if (education >= 40) baseScore = random.Next(6, 14);
            else baseScore = random.Next(0, 6);

            var bonus = state.HasFlag("exam_bonus_2") ? 3 : (state.HasFlag("exam_bonus_1") ? 1 : 0);
            score = Math.Min(30, baseScore + bonus);

            state.SetFlag("sat_hkcee");

            if (score >= 14)
            {
                state.SetFlag("matriculated"); // Eligible for Form 6 matriculation
                return $"【香港中學會考 HKCEE】你獲得了 {score} 分！成功達到 14 分中六升學門檻！(包含選修科答題加分: +{bonus}分)";
            }
            else
            {
                return $"【香港中學會考 HKCEE】你獲得了 {score} 分。未能升讀中六，即將投入社會工作。(包含選修科答題加分: +{bonus}分)";
            }
        }
    }

    public static string RunUniversityAdmission(GameState state, int examScore)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.EraId == "2024plus")
        {
            // JUPAS Admission
            if (examScore >= 30)
            {
                state.SetFlag("university_student");
                state.SetFlag("uni_elite");
                state.Stats = state.Stats.ApplyDelta(new StatDelta(Reputation: 15, Money: -5000));
                return "🎓 JUPAS 放榜：你成功考入香港大學神科【內外全科醫學士 MBBS】！前途無限！";
            }
            else if (examScore >= 18)
            {
                state.SetFlag("university_student");
                state.Stats = state.Stats.ApplyDelta(new StatDelta(Reputation: 5, Money: -5000));
                return "🎓 JUPAS 放榜：你獲香港中文大學錄取，入讀熱門學士學位課程。";
            }
            else if (examScore >= 12)
            {
                state.SetFlag("college_student");
                state.Stats = state.Stats.ApplyDelta(new StatDelta(Reputation: 1, Money: -8000));
                return "🎓 JUPAS 放榜：你獲派副學士/高級文憑 (Associate Degree / Higher Diploma) 課程。";
            }
            else
            {
                state.SetFlag("early_worker");
                state.Stats = state.Stats.ApplyDelta(new StatDelta(Reputation: -2, Money: 2000));
                return "❌ JUPAS 放榜：你落選所有大學課程，唯有直接出嚟工作搵食。";
            }
        }
        else
        {
            // 1960s - 2000s path:
            if (state.HasFlag("matriculated"))
            {
                // Conduct HKALE (高考)
                var random = Random.Shared;
                var alePassed = state.Stats.Education >= 55 && random.Next(100) < 70;
                if (alePassed)
                {
                    state.SetFlag("university_student");
                    state.Stats = state.Stats.ApplyDelta(new StatDelta(Reputation: 12, Money: -1000));
                    var uName = state.EraId == "1960s" ? "香港大學" : "香港科技大學";
                    return $"🎓 高考放榜 (HKALE)：你順利通過高考，獲【{uName}】錄取就讀學士學位！";
                }
                else
                {
                    state.SetFlag("early_worker");
                    state.Stats = state.Stats.ApplyDelta(new StatDelta(Money: 1500));
                    return "❌ 高考放榜 (HKALE)：你高考成績未達大學入學門檻，決定出社會打工。";
                }
            }
            else
            {
                state.SetFlag("early_worker");
                state.Stats = state.Stats.ApplyDelta(new StatDelta(Money: 1500));
                return "💼 你沒有達到中六資格，正式進入職場成為一位打工仔。";
            }
        }
    }
}
