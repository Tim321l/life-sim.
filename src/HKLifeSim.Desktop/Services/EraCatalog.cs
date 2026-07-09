namespace HKLifeSim.Desktop.Services;

internal sealed record EraCardInfo(string EraId, string DisplayName, string YearRange, string Description);

internal static class EraCatalog
{
    private static readonly Dictionary<string, EraCardInfo> Cards = new(StringComparer.Ordinal)
    {
        ["1960s"] = new EraCardInfo("1960s", "六十年代", "1960–1979", "戰後香港，制水年代，白手興家的歲月。"),
        ["1980s"] = new EraCardInfo("1980s", "八十年代", "1980–1999", "中英談判，移民潮起，經濟起飛的黃金時代。"),
        ["2000s"] = new EraCardInfo("2000s", "千禧年代", "2000–2023", "沙士、金融海嘯與科網爆破的動盪年代。"),
        ["2024plus"] = new EraCardInfo("2024plus", "現代", "2024–2045", "樓價高企，斜槓當道，AI 時代的香港。"),
    };

    public static EraCardInfo Describe(string eraId) =>
        Cards.TryGetValue(eraId, out var info) ? info : new EraCardInfo(eraId, eraId, string.Empty, string.Empty);
}
