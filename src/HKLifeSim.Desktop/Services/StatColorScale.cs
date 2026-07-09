using Avalonia.Media;

namespace HKLifeSim.Desktop.Services;

internal static class StatColorScale
{
    private static readonly Color Good = Color.FromRgb(0x2E, 0x7D, 0x32);
    private static readonly Color Warning = Color.FromRgb(0xF9, 0xA8, 0x25);
    private static readonly Color Bad = Color.FromRgb(0xC6, 0x28, 0x28);

    /// <summary>Green at the healthy end, red at the unhealthy end, for a stat clamped to 0..100.</summary>
    public static IBrush ForStat(int value, bool highIsGood)
    {
        var clamped = Math.Clamp(value, 0, 100);
        var healthiness = highIsGood ? clamped : 100 - clamped;

        var color = healthiness >= 50
            ? Lerp(Warning, Good, (healthiness - 50) / 50.0)
            : Lerp(Bad, Warning, healthiness / 50.0);

        return new SolidColorBrush(color);
    }

    private static Color Lerp(Color from, Color to, double t)
    {
        var clampedT = Math.Clamp(t, 0.0, 1.0);
        byte Channel(byte a, byte b) => (byte)(a + ((b - a) * clampedT));
        return Color.FromRgb(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }
}
