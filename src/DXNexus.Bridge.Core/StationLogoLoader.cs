using System.Collections.Concurrent;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace DXNexus.Bridge.Core;

public sealed class StationLogoLoader(HttpClient httpClient)
{
    private const int MaximumDownloadBytes = 2 * 1024 * 1024;
    private const int MaximumPngBytes = 32 * 1024;
    private const int OutputSize = 48;
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.Ordinal);
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<byte[]?> LoadPngAsync(string? logoUrl, CancellationToken cancellationToken = default)
    {
        var uri = ResolveLogoUri(logoUrl, _httpClient.BaseAddress ?? PairingApiClient.ProductionBaseUri);
        if (uri is null) return null;
        if (_cache.TryGetValue(uri.AbsoluteUri, out var cached)) return cached;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes) return null;

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var encoded = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (encoded.Length + read > MaximumDownloadBytes) return null;
            await encoded.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        using var image = Image.Load(encoded.ToArray());
        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(OutputSize, OutputSize),
        }));
        using var output = new MemoryStream();
        await image.SaveAsPngAsync(output, cancellationToken).ConfigureAwait(false);
        if (output.Length > MaximumPngBytes) return null;

        var png = output.ToArray();
        if (_cache.Count >= 256) _cache.Clear();
        _cache.TryAdd(uri.AbsoluteUri, png);
        return png;
    }

    public static Uri? ResolveLogoUri(string? logoUrl, Uri apiBaseUri)
    {
        ArgumentNullException.ThrowIfNull(apiBaseUri);
        if (string.IsNullOrWhiteSpace(logoUrl)) return null;
        if (!Uri.TryCreate(logoUrl.Trim(), UriKind.RelativeOrAbsolute, out var parsed)) return null;

        var resolved = parsed.IsAbsoluteUri ? parsed : new Uri(apiBaseUri, parsed);
        return resolved.Scheme == Uri.UriSchemeHttps ? resolved : null;
    }
}
