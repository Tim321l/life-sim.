using System.Globalization;

namespace HKLifeSim.Web.Services;

internal static class StatColorScale
{
    private static readonly (byte R, byte G, byte B) Good = (0x2E, 0x7D, 0x32);
    private static readonly (byte R, byte G, byte B) Warning = (0xF9, 0xA8, 0x25);
    private static readonly (byte R, byte G, byte B) Bad = (0xC6, 0x28, 0x28);

    /// <summary>CSS hex color: green at the healthy end, red at the unhealthy end, for a stat clamped to 0..100.</summary>
    public static string ForStat(int value, bool highIsGood)
    {
        var clamped = Math.Clamp(value, 0, 100);
        var healthiness = highIsGood ? clamped : 100 - clamped;

        var color = healthiness >= 50
            ? Lerp(Warning, Good, (healthiness - 50) / 50.0)
            : Lerp(Bad, Warning, healthiness / 50.0);

        return string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private static (byte R, byte G, byte B) Lerp((byte R, byte G, byte B) from, (byte R, byte G, byte B) to, double t)
    {
        var clampedT = Math.Clamp(t, 0.0, 1.0);
        byte Channel(byte a, byte b) => (byte)(a + ((b - a) * clampedT));
        return (Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }
}
