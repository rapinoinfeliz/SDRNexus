using System.Text.Json;
using DXNexus.Contracts;

namespace DXNexus.Bridge.Core;

public sealed record BridgePreferences(Guid? ListeningPointId, Guid? ReceiverProfileId, bool LiveBrowserCompanion = false)
{
    public ReceptionSetupContext? ReceptionSetup => ListeningPointId is Guid point && ReceiverProfileId is Guid receiver
        ? new ReceptionSetupContext(point, receiver)
        : null;
}

public sealed class BridgePreferencesStore(string? filePath = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DXNexus",
        "bridge-preferences.json");

    public async Task<BridgePreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath)) return new BridgePreferences(null, null, false);
        try
        {
            await using var input = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<BridgePreferences>(input, JsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? new BridgePreferences(null, null, false);
        }
        catch (JsonException)
        {
            return new BridgePreferences(null, null, false);
        }
    }

    public async Task SaveAsync(BridgePreferences preferences, CancellationToken cancellationToken = default)
    {
        var parent = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Preferences path has no parent directory.");
        Directory.CreateDirectory(parent);
        var temporary = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(output, preferences, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            File.Move(temporary, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
