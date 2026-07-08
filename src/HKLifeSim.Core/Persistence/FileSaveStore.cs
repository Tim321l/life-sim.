namespace HKLifeSim.Core.Persistence;

public sealed class FileSaveStore : ISaveStore
{
    private const int MaxSlotNameLength = 64;

    private readonly string _baseDirectory;

    public FileSaveStore(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
        Directory.CreateDirectory(_baseDirectory);
    }

    public async Task WriteAsync(string slot, string json, CancellationToken cancellationToken = default)
    {
        ValidateSlotName(slot);
        var finalPath = GetPath(slot);
        var tempPath = finalPath + ".tmp";

        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, finalPath, overwrite: true);
    }

    public async Task<string?> ReadAsync(string slot, CancellationToken cancellationToken = default)
    {
        ValidateSlotName(slot);
        var path = GetPath(slot);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> slots = Directory.EnumerateFiles(_baseDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();

        return Task.FromResult(slots);
    }

    public Task DeleteAsync(string slot, CancellationToken cancellationToken = default)
    {
        ValidateSlotName(slot);
        var path = GetPath(slot);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetPath(string slot) => Path.Combine(_baseDirectory, $"{slot}.json");

    private static void ValidateSlotName(string slot)
    {
        if (string.IsNullOrEmpty(slot))
        {
            throw new ArgumentException("Save slot name must not be empty.", nameof(slot));
        }

        if (slot.Length > MaxSlotNameLength)
        {
            throw new ArgumentException($"Save slot name must not exceed {MaxSlotNameLength} characters.", nameof(slot));
        }

        if (slot.Contains('/', StringComparison.Ordinal) ||
            slot.Contains('\\', StringComparison.Ordinal) ||
            slot.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Save slot name must not contain path separators or '..'.", nameof(slot));
        }
    }
}
