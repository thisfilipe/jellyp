using Jellyfin.Plugin.JellyP.Conversion;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.JellyP.Tests;

public sealed class WebpImageConverterTests
{
    private readonly WebpImageConverter _converter = new();

    [Fact]
    public void ConvertToWebp_ProducesWebpAndPreservesDimensionsAndAlpha()
    {
        var source = CreatePng(32, 16, static (x, _) => x switch
        {
            < 8 => new SKColor(240, 20, 30, 0),
            < 16 => new SKColor(240, 20, 30, 128),
            _ => new SKColor(20, 80, 220, 255),
        });

        var result = _converter.ConvertToWebp(source, quality: 85);

        Assert.True(result.Length > 12);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(result, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(result, 8, 4));

        using var data = SKData.CreateCopy(result);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        using var bitmap = SKBitmap.Decode(codec!);
        Assert.NotNull(bitmap);
        Assert.Equal(32, bitmap!.Width);
        Assert.Equal(16, bitmap.Height);
        Assert.Equal((byte)0, bitmap.GetPixel(2, 4).Alpha);
        Assert.Equal((byte)128, bitmap.GetPixel(12, 4).Alpha);
        Assert.Equal((byte)255, bitmap.GetPixel(24, 4).Alpha);
    }

    [Fact]
    public void ConvertToWebp_RejectsInvalidImageData()
    {
        Assert.Throws<InvalidDataException>(() => _converter.ConvertToWebp([1, 2, 3, 4]));
    }

    [Fact]
    public void ConvertToWebp_RejectsImagesAboveDimensionLimit()
    {
        var source = CreatePng(
            WebpImageConverter.MaximumDimension + 1,
            1,
            static (_, _) => new SKColor(20, 80, 220, 255));

        Assert.Throws<NotSupportedException>(() => _converter.ConvertToWebp(source));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ConvertToWebp_RejectsQualityOutsideSupportedRange(int quality)
    {
        var source = CreatePng(2, 2, static (_, _) => new SKColor(20, 80, 220, 255));

        Assert.Throws<ArgumentOutOfRangeException>(() => _converter.ConvertToWebp(source, quality));
    }

    [Fact]
    public void Optimize_ResizesWithAspectRatioAndPreservesEveryAlphaSample()
    {
        var source = CreatePng(602, 302, static (x, y) => NoiseColor(x, y, (byte)(x + y)));
        var result = _converter.Optimize(source, PermissiveOptions() with { MaximumDimension = 301 });
        Assert.True(result.Success, result.SkipReason.ToString());
        Assert.Equal((602, 302), (result.SourceWidth, result.SourceHeight));
        Assert.Equal((301, 151), (result.OutputWidth, result.OutputHeight));
        // Decode to the same explicit unpremultiplied representation used by the converter.
        using var sourceData = SKData.CreateCopy(source);
        using var sourceCodec = SKCodec.Create(sourceData);
        using var unpremultiplied = new SKBitmap(new SKImageInfo(602, 302, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Assert.Equal(SKCodecResult.Success, sourceCodec.GetPixels(unpremultiplied.Info, unpremultiplied.GetPixels()));
        using var expected = unpremultiplied.Resize(new SKImageInfo(301, 151, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        using var output = SKBitmap.Decode(result.OutputBytes);
        for (var y = 0; y < output.Height; y++)
        for (var x = 0; x < output.Width; x++)
            Assert.Equal(expected.GetPixel(x, y).Alpha, output.GetPixel(x, y).Alpha);
    }

    [Theory]
    [InlineData(320, 160)]
    [InlineData(321, 161)]
    [InlineData(1, 1)]
    public void Optimize_DoesNotUpscaleAndConvertsJpeg(int width, int height)
    {
        var source = CreateJpeg(width, height);
        var result = _converter.Optimize(source, PermissiveOptions() with { MaximumDimension = 640 });
        Assert.True(result.Success, result.SkipReason.ToString());
        Assert.Equal((width, height), (result.OutputWidth, result.OutputHeight));
        Assert.NotNull(result.OutputBytes);
        Assert.Equal(result.OutputBytes.Length, result.CandidateBytes);
        Assert.Equal(source.Length, result.SourceBytes);
        Assert.InRange(result.Ssim!.Value, 0, 1);
    }

    [Fact]
    public void Optimize_PreservesAllAlphaValuesWithoutResize()
    {
        var source = CreatePng(256, 128, static (x, y) => NoiseColor(x, y, (byte)x));
        var result = _converter.Optimize(source, PermissiveOptions());
        Assert.True(result.Success, result.SkipReason.ToString());
        using var output = SKBitmap.Decode(result.OutputBytes);
        for (var y = 0; y < 128; y++)
        for (var x = 0; x < 256; x++)
            Assert.Equal((byte)x, output.GetPixel(x, y).Alpha);
    }

    [Fact]
    public void Optimize_RejectsCandidateBelowQualityThreshold()
    {
        var source = CreatePng(256, 256, static (x, y) => new SKColor((byte)(x * 37 + y * 13), (byte)(x * 17 + y * 41), (byte)(x * 53 + y * 7)));
        var result = _converter.Optimize(source, PermissiveOptions() with { Quality = 75, MinimumSsim = 1 });
        Assert.Equal(WebpSkipReason.QualityBelowThreshold, result.SkipReason);
        Assert.Null(result.OutputBytes);
        Assert.True(result.CandidateBytes > 0);
        Assert.True(result.Ssim < 1);
    }

    [Theory]
    [InlineData(1000000, 0)]
    [InlineData(0, 1)]
    public void Optimize_RequiresBothSavingsThresholds(int bytes, double ratio)
    {
        var source = CreatePng(256, 256, static (x, y) => new SKColor((byte)(x * 37 + y * 13), (byte)(x * 17 + y * 41), (byte)(x * 53 + y * 7)));
        var result = _converter.Optimize(source, PermissiveOptions() with { MinimumSavingsBytes = bytes, MinimumSavingsRatio = ratio });
        Assert.Equal(WebpSkipReason.InsufficientSavings, result.SkipReason);
        Assert.Null(result.OutputBytes);
        Assert.True(result.CandidateBytes > 0);
    }

    [Fact]
    public void Optimize_DefaultsRequireMeaningfulSavings()
    {
        var source = CreatePng(32, 32, static (_, _) => new SKColor(30, 80, 140));
        var result = _converter.Optimize(source);
        Assert.Equal(WebpSkipReason.InsufficientSavings, result.SkipReason);
    }

    [Fact]
    public void Optimize_RejectsTruncatedPngEvenWhenPixelDataIsPresent()
    {
        var source = CreatePng(64, 64, static (x, y) => new SKColor((byte)x, (byte)y, 120));
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(source.AsSpan(0, source.Length - 12)).SkipReason);
        var corrupt = source.ToArray();
        corrupt[50] ^= 1;
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(corrupt).SkipReason);
        // A complete PNG container does not imply that the compressed scanlines are complete.
        var noise = CreatePng(128, 128, static (x, y) => NoiseColor(x, y, 255));
        var offset = 8;
        while (!noise.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            offset += System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(noise.AsSpan(offset, 4)) + 12;
        var dataLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(noise.AsSpan(offset, 4));
        var shortenedData = noise.AsSpan(offset + 8, dataLength / 2).ToArray();
        var truncated = noise[..offset].Concat(CreatePngChunk("IDAT", shortenedData)).Concat(noise[^12..]).ToArray();
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(truncated).SkipReason);
    }

    [Fact]
    public void Optimize_RejectsTruncatedJpegWithAndWithoutEndMarker()
    {
        var source = CreateJpeg(256, 256);
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(source.AsSpan(0, source.Length - 2)).SkipReason);
        var truncated = source[..(source.Length / 2)].Concat(new byte[] { 255, 217 }).ToArray();
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(truncated).SkipReason);
    }

    [Fact]
    public void Optimize_RejectsAnimationAndUnsupportedMetadata()
    {
        var png = CreatePng(32, 32, static (_, _) => new SKColor(40, 70, 90));
        Assert.Equal(WebpSkipReason.AnimatedImage, _converter.Optimize(InsertPngChunk(png, "acTL", new byte[] { 0, 0, 0, 2, 0, 0, 0, 0 })).SkipReason);
        Assert.Equal(WebpSkipReason.UnsupportedColorProfile, _converter.Optimize(InsertPngChunk(png, "iCCP", new byte[] { 65, 0, 0, 1 })).SkipReason);
        Assert.Equal(WebpSkipReason.UnsupportedOrientation, _converter.Optimize(InsertPngChunk(png, "eXIf", new byte[] { 1 })).SkipReason);
        var jpeg = CreateJpeg(32, 32);
        var profile = new byte[] { 255, 226, 0, 16 }.Concat("ICC_PROFILE\0"u8.ToArray()).Concat(new byte[] { 1, 1 }).ToArray();
        Assert.Equal(WebpSkipReason.UnsupportedColorProfile, _converter.Optimize(jpeg[..2].Concat(profile).Concat(jpeg[2..]).ToArray()).SkipReason);
        var progressive = jpeg.ToArray();
        var sof = progressive.AsSpan().IndexOf(new byte[] { 255, 192 });
        Assert.True(sof >= 0);
        progressive[sof + 1] = 193;
        Assert.Equal(WebpSkipReason.UnsupportedFormat, _converter.Optimize(progressive).SkipReason);
        // EXIF TIFF little endian, orientation=6 (90 degrees clockwise).
        var exif = new byte[] { 255, 225, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0, 1, 0, 18, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Equal(WebpSkipReason.UnsupportedOrientation, _converter.Optimize(jpeg[..2].Concat(exif).Concat(jpeg[2..]).ToArray()).SkipReason);
    }

    [Fact]
    public void Optimize_RejectsUnsupportedFormatsAndLimits()
    {
        var png = CreatePng(32, 32, static (_, _) => new SKColor(10, 40, 90));
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        Assert.Equal(WebpSkipReason.UnsupportedFormat, _converter.Optimize(gif).SkipReason);
        Assert.Equal(WebpSkipReason.InputLimitExceeded, _converter.Optimize(new byte[WebpImageConverter.MaximumInputBytes + 1]).SkipReason);
        var tooWide = CreatePng(WebpImageConverter.MaximumDimension + 1, 1, static (_, _) => SKColors.Black);
        Assert.Equal(WebpSkipReason.InputLimitExceeded, _converter.Optimize(tooWide).SkipReason);
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(new byte[] { 1, 2, 3 }).SkipReason);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Optimize_RejectsInvalidOptions(int quality)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _converter.Optimize(new byte[] { 1 }, new WebpOptimizationOptions { Quality = quality }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _converter.Optimize(new byte[] { 1 }, new WebpOptimizationOptions { MinimumSsim = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _converter.Optimize(new byte[] { 1 }, new WebpOptimizationOptions { MaximumDimension = 0 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(100)]
    public void Optimize_AcceptsExplicitQualityAcrossFullRange(int quality)
    {
        var source = CreatePng(128, 128, static (x, y) => NoiseColor(x, y, 255));
        var result = _converter.Optimize(source, PermissiveOptions() with { Quality = quality });
        Assert.True(result.Success, result.SkipReason.ToString());
    }

    [Fact]
    public void Optimize_LosslessVerifiesEveryDecodedRgbaSample()
    {
        var source = CreatePng(128, 128, static (x, y) => NoiseColor(x, y, (byte)(1 + ((x + y) % 255))));
        source = InsertPngChunk(source, "tEXt", new byte[50000]);
        var result = _converter.Optimize(source, PermissiveOptions() with { Lossless = true, Quality = 60 });
        Assert.True(result.Success, result.SkipReason.ToString());
        Assert.Equal(1d, result.Ssim);
        using var sourceData = SKData.CreateCopy(source);
        using var sourceCodec = SKCodec.Create(sourceData);
        using var original = new SKBitmap(new SKImageInfo(128, 128, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Assert.Equal(SKCodecResult.Success, sourceCodec.GetPixels(original.Info, original.GetPixels()));
        using var outputData = SKData.CreateCopy(result.OutputBytes);
        using var outputCodec = SKCodec.Create(outputData);
        using var decoded = new SKBitmap(original.Info);
        Assert.Equal(SKCodecResult.Success, outputCodec.GetPixels(decoded.Info, decoded.GetPixels()));
        Assert.True(original.GetPixelSpan().SequenceEqual(decoded.GetPixelSpan()));
    }

    [Fact]
    public void Optimize_LosslessRejectsNormalizationOfInvisibleRgb()
    {
        var source = InsertPngChunk(CreateRawTransparentPng(), "tEXt", new byte[5000]);
        var result = _converter.Optimize(source, PermissiveOptions() with { Lossless = true });
        Assert.Equal(WebpSkipReason.PixelsChanged, result.SkipReason);
        Assert.Null(result.OutputBytes);
    }

    [Fact]
    public void Optimize_AcceptsStaticWebpAndRejectsInvalidRiffOrAnimation()
    {
        var png = CreatePng(128, 128, static (x, y) => NoiseColor(x, y, 255));
        var webp = _converter.ConvertToWebp(png, 100);
        var result = _converter.Optimize(webp, PermissiveOptions() with { Quality = 60 });
        Assert.True(result.Success, result.SkipReason.ToString());
        Assert.Equal((128, 128), (result.SourceWidth, result.SourceHeight));
        var incorrectSize = webp.ToArray();
        incorrectSize[4] ^= 1;
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(incorrectSize).SkipReason);
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(webp[..^1]).SkipReason);
        var truncated = webp[..(webp.Length / 2)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(truncated.AsSpan(4), truncated.Length - 8);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(truncated.AsSpan(16), truncated.Length - 20);
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(truncated).SkipReason);
        var animated = new byte[30];
        "RIFF"u8.CopyTo(animated);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(animated.AsSpan(4), 22);
        "WEBPVP8X"u8.CopyTo(animated.AsSpan(8));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(animated.AsSpan(16), 10);
        animated[20] = 2;
        Assert.Equal(WebpSkipReason.AnimatedImage, _converter.Optimize(animated).SkipReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Optimize_AcceptsLossyAndLosslessWebpWithAlpha(bool lossless)
    {
        var png = CreatePng(128, 128, static (x, y) => NoiseColor(x, y, (byte)x));
        using var sourceData = SKData.CreateCopy(png);
        using var sourceCodec = SKCodec.Create(sourceData);
        using var bitmap = new SKBitmap(new SKImageInfo(128, 128, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Assert.Equal(SKCodecResult.Success, sourceCodec.GetPixels(bitmap.Info, bitmap.GetPixels()));
        using var pixmap = bitmap.PeekPixels();
        using var encoded = pixmap.Encode(new SKWebpEncoderOptions(lossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy, 100));
        Assert.NotNull(encoded);
        var result = _converter.Optimize(encoded.ToArray(), PermissiveOptions() with { Quality = 60 });
        Assert.True(result.Success, result.SkipReason.ToString());
        Assert.Equal((128, 128), (result.OutputWidth, result.OutputHeight));
    }

    private static byte[] CreateRawTransparentPng()
    {
        byte[] header = [0, 0, 0, 16, 0, 0, 0, 16, 8, 6, 0, 0, 0];
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, true))
        {
            for (var y = 0; y < 16; y++)
            {
                zlib.WriteByte(0);
                for (var x = 0; x < 16; x++) zlib.Write(new byte[] { 80, 40, 20, 0 });
            }
        }
        return new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.Concat(CreatePngChunk("IHDR", header))
            .Concat(CreatePngChunk("IDAT", compressed.ToArray())).Concat(CreatePngChunk("IEND", [])).ToArray();
    }

    private static WebpOptimizationOptions PermissiveOptions() => new() { MinimumSsim = 0, MinimumSavingsBytes = 0, MinimumSavingsRatio = 0 };

    private static byte[] CreateJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            bitmap.SetPixel(x, y, NoiseColor(x, y, 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return encoded.ToArray();
    }

    private static SKColor NoiseColor(int x, int y, byte alpha)
    {
        var seed = unchecked(((uint)x * 374761393) + ((uint)y * 668265263));
        seed = (seed ^ (seed >> 13)) * 1274126177;
        seed ^= seed >> 16;
        return new SKColor((byte)seed, (byte)(seed >> 8), (byte)(seed >> 16), alpha);
    }

    private static byte[] InsertPngChunk(byte[] png, string type, byte[] data)
        => png[..33].Concat(CreatePngChunk(type, data)).Concat(png[33..]).ToArray();

    private static byte[] CreatePngChunk(string type, byte[] data)
    {
        var chunk = new byte[data.Length + 12];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        uint crc = uint.MaxValue;
        foreach (var value in chunk.AsSpan(4, data.Length + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(data.Length + 8), crc ^ uint.MaxValue);
        return chunk;
    }

    private static byte[] CreatePng(int width, int height, Func<int, int, SKColor> colorAt)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul));

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, colorAt(x, y));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return data?.ToArray() ?? throw new InvalidOperationException("Could not create the PNG fixture.");
    }
}
