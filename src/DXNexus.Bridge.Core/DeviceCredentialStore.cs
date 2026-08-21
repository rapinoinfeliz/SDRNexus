using System.Security.Cryptography;
using System.Text.Json;

namespace DXNexus.Bridge.Core;

public interface IDeviceCredentialStore
{
    bool Exists { get; }
    Task SaveAsync(DeviceCredential credential, CancellationToken cancellationToken = default);
    Task<DeviceCredential?> LoadAsync(CancellationToken cancellationToken = default);
    void Delete();
}

public sealed class DeviceCredentialStore(string? filePath = null) : IDeviceCredentialStore
{
    private static readonly byte[] Entropy = "DXNexus.SDRNexus.DeviceCredential.v1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DXNexus",
        "device-credential.bin");

    public bool Exists => File.Exists(_filePath);

    public async Task SaveAsync(DeviceCredential credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var parent = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Credential path has no parent directory.");
        Directory.CreateDirectory(parent);
        var clear = JsonSerializer.SerializeToUtf8Bytes(credential, JsonOptions);
        var protectedBytes = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(clear);
        var temporary = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<DeviceCredential?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(_filePath, cancellationToken).ConfigureAwait(false);
        var clear = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return JsonSerializer.Deserialize<DeviceCredential>(clear, JsonOptions)
                ?? throw new InvalidDataException("The saved DXNexus device credential is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    public void Delete()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }
}
