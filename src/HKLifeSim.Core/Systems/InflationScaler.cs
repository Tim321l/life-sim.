using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Systems;

public static class InflationScaler
{
    public static StatDelta Scale(StatDelta delta, EraConfig era)
    {
        ArgumentNullException.ThrowIfNull(era);

        if (delta.Money == 0)
        {
            return delta;
        }

        var scaled = delta.Scale(era.InflationMultiplier);
        if (scaled.Money == 0)
        {
            scaled = scaled with { Money = Math.Sign(delta.Money) };
        }

        return scaled;
    }
}
