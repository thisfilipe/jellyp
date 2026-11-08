using System.Text.Json;
using MediaBrowser.Common.Plugins;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.JellyP.Tests;

public sealed class BrandingTests
{
    [Fact]
    public void ExternalPluginManifestRoundTripsImagePathButNotBundledResourceName()
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>("""{"imagePath":"jellyp-plugin.png","imageResourceName":"not-an-external-manifest-field"}""");
        Assert.NotNull(manifest);
        Assert.Equal("jellyp-plugin.png", manifest.ImagePath);
        Assert.Null(manifest.ImageResourceName);
    }

    [Fact]
    public void InstalledCardAddsOnlyTransparentMarginsWithoutChangingSymbolPixels()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.JellyP.Web.branding.jellyp-symbol.png");
        using var symbol = SKBitmap.Decode(stream);
        using var card = SKBitmap.Decode(Path.Combine(AppContext.BaseDirectory, "branding", "jellyp-plugin.png"));
        Assert.Equal(768, card.Width);
        Assert.Equal(432, card.Height);
        for (var y = 0; y < card.Height; y++)
        {
            for (var x = 0; x < card.Width; x++)
            {
                var pixel = card.GetPixel(x, y);
                if (x >= 256 && x < 512 && y >= 88 && y < 344)
                    Assert.Equal(symbol.GetPixel(x - 256, y - 88), pixel);
                else
                    Assert.Equal(0, pixel.Alpha);
            }
        }
    }

    [Theory]
    [InlineData("jellyp-symbol.png", 256, 256)]
    [InlineData("jellyp-wordmark-dark.png", 480, 159)]
    [InlineData("jellyp-wordmark-light.png", 480, 159)]
    public void RuntimeBrandingIsEmbeddedPngWithTransparentMargins(string name, int width, int height)
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.JellyP.Web.branding." + name);
        Assert.NotNull(stream);
        Assert.InRange(stream.Length, 1, 40_000);
        using var bitmap = SKBitmap.Decode(stream);
        Assert.Equal(width, bitmap.Width);
        Assert.Equal(height, bitmap.Height);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha == 255);
    }
}
