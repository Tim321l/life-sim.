namespace HKLifeSim.Desktop.Services;

internal static class DeathCauseTranslator
{
    private static readonly Dictionary<string, string> Translations = new(StringComparer.Ordinal)
    {
        ["health_zero"] = "油盡燈枯",
        ["stress_breakdown"] = "情緒崩潰",
        ["old_age"] = "壽終正寢",
    };

    public static string Translate(string? deathCause) =>
        deathCause is not null && Translations.TryGetValue(deathCause, out var translated) ? translated : "命運無常";
}
