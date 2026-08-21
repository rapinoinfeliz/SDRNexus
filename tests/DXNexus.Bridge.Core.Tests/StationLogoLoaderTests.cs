using System.Net;
using System.Net.Http.Headers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace DXNexus.Bridge.Core.Tests;

public sealed class StationLogoLoaderTests
{
    [Fact]
    public async Task RelativeWebpLogoIsResolvedResizedAndConvertedToPng()
    {
        using var source = new Image<Rgba32>(96, 48, new Rgba32(20, 120, 220, 255));
        using var webp = new MemoryStream();
        await source.SaveAsWebpAsync(webp);
        using var handler = new LogoHandler(webp.ToArray(), "image/webp");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://dxnexus.example/api/sdr/v1/"),
        };
        var loader = new StationLogoLoader(httpClient);

        var png = await loader.LoadPngAsync("/data/radio/logos/test.webp");

        Assert.NotNull(png);
        Assert.Equal(new Uri("https://dxnexus.example/data/radio/logos/test.webp"), handler.RequestUri);
        using var rendered = Image.Load(png);
        Assert.Equal(48, rendered.Width);
        Assert.Equal(24, rendered.Height);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
    }

    [Fact]
    public async Task InsecureLogoUrlIsRejectedWithoutNetworkRequest()
    {
        using var handler = new LogoHandler([], "image/png");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://dxnexus.example/api/sdr/v1/"),
        };
        var loader = new StationLogoLoader(httpClient);

        var png = await loader.LoadPngAsync("http://example.test/logo.png");

        Assert.Null(png);
        Assert.Null(handler.RequestUri);
    }

    private sealed class LogoHandler(byte[] content, string contentType) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            var body = new ByteArrayContent(content);
            body.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }
    }
}
