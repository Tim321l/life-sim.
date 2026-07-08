namespace HKLifeSim.Core.Persistence;

public interface ISaveStore
{
    Task WriteAsync(string slot, string json, CancellationToken cancellationToken = default);

    Task<string?> ReadAsync(string slot, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(string slot, CancellationToken cancellationToken = default);
}
