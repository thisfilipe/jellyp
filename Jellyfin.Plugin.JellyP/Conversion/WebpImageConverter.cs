using System.Buffers.Binary;
using SkiaSharp;

namespace Jellyfin.Plugin.JellyP.Conversion;

/// <summary>Creates bounded, verified WebP candidates without touching source files.</summary>
public sealed class WebpImageConverter
{
    /// <summary>Maximum encoded input size (32 MiB).</summary>
    public const int MaximumInputBytes = 32 * 1024 * 1024;
    /// <summary>Maximum decoded source edge.</summary>
    public const int MaximumDimension = 8192;
    /// <summary>Maximum decoded source area.</summary>
    public const long MaximumPixelCount = 40_000_000;
    private static readonly uint[] CrcTable = CreateCrcTable();

    /// <summary>Optimizes one static JPEG, PNG or WebP and returns evidence for every rejection.</summary>
    public WebpOptimizationResult Optimize(ReadOnlySpan<byte> source, WebpOptimizationOptions? options = null)
    {
        options ??= new WebpOptimizationOptions();
        ValidateOptions(options);
        return Convert(source, options, enforceGates: true);
    }

    /// <summary>Compatibility conversion without resizing or savings/SSIM gates.</summary>
    /// <param name="source">Encoded static JPEG, PNG or WebP bytes.</param>
    /// <param name="quality">WebP quality from 0 through 100.</param>
    /// <returns>Verified encoded WebP bytes.</returns>
    public byte[] ConvertToWebp(ReadOnlySpan<byte> source, int quality = 85)
    {
        if (source.IsEmpty) throw new ArgumentException("Image data must not be empty.", nameof(source));
        if (source.Length > MaximumInputBytes) throw new ArgumentOutOfRangeException(nameof(source));
        if (quality is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(quality));
        var result = Convert(source, new WebpOptimizationOptions { Quality = quality, MaximumDimension = MaximumDimension }, false);
        if (result.Success) return result.OutputBytes!;
        if (result.SkipReason is WebpSkipReason.InputLimitExceeded or WebpSkipReason.UnsupportedFormat
            or WebpSkipReason.AnimatedImage or WebpSkipReason.UnsupportedOrientation or WebpSkipReason.UnsupportedColorProfile)
            throw new NotSupportedException($"The image cannot be converted: {result.SkipReason}.");
        throw new InvalidDataException($"The image could not be converted completely: {result.SkipReason}.");
    }

    private static void ValidateOptions(WebpOptimizationOptions options)
    {
        if (options.Quality is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(options), "Quality must be 0 through 100.");
        if (options.MaximumDimension is < 1 or > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(options), "Maximum dimension must be 1 through 8192.");
        if (!double.IsFinite(options.MinimumSsim) || options.MinimumSsim is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(options));
        if (!double.IsFinite(options.MinimumSavingsRatio) || options.MinimumSavingsRatio is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.MinimumSavingsBytes < 0) throw new ArgumentOutOfRangeException(nameof(options));
    }

    private static WebpOptimizationResult Convert(ReadOnlySpan<byte> source, WebpOptimizationOptions options, bool enforceGates)
    {
        var result = new WebpOptimizationResult { SourceBytes = source.Length };
        if (source.IsEmpty) return result with { SkipReason = WebpSkipReason.InvalidInput };
        if (source.Length > MaximumInputBytes) return result with { SkipReason = WebpSkipReason.InputLimitExceeded };
        var structure = CheckStructure(source);
        if (structure != WebpSkipReason.None) return result with { SkipReason = structure };

        using var sourceData = SKData.CreateCopy(source);
        using var codec = SKCodec.Create(sourceData);
        if (codec is null) return result with { SkipReason = WebpSkipReason.InvalidInput };
        var info = codec.Info;
        result = result with { SourceWidth = info.Width, SourceHeight = info.Height };
        if (info.Width <= 0 || info.Height <= 0 || info.Width > MaximumDimension || info.Height > MaximumDimension
            || (long)info.Width * info.Height > MaximumPixelCount)
            return result with { SkipReason = WebpSkipReason.InputLimitExceeded };
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            return result with { SkipReason = WebpSkipReason.UnsupportedFormat };
        if (codec.FrameCount > 1) return result with { SkipReason = WebpSkipReason.AnimatedImage };
        if (codec.EncodedOrigin != SKEncodedOrigin.TopLeft) return result with { SkipReason = WebpSkipReason.UnsupportedOrientation };
        if (info.ColorSpace is { IsSrgb: false }) return result with { SkipReason = WebpSkipReason.UnsupportedColorProfile };

        // GetPixels must report full success. SKBitmap.Decode can return a partially filled image.
        using var decoded = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
            return result with { SkipReason = WebpSkipReason.InvalidInput };
        var scale = Math.Min(1d, (double)options.MaximumDimension / Math.Max(info.Width, info.Height));
        var width = Math.Max(1, (int)Math.Round(info.Width * scale));
        var height = Math.Max(1, (int)Math.Round(info.Height * scale));
        result = result with { OutputWidth = width, OutputHeight = height };
        using var resized = scale < 1 ? decoded.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)) : null;
        var baseline = resized ?? decoded;
        if (baseline.Width != width || baseline.Height != height) return result with { SkipReason = WebpSkipReason.InvalidOutput };
        using var pixmap = baseline.PeekPixels();
        using var encoded = pixmap.Encode(new SKWebpEncoderOptions(
            options.Lossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy,
            options.Quality));
        if (encoded is null || encoded.Size <= 0 || encoded.Size > MaximumInputBytes)
            return result with { SkipReason = WebpSkipReason.InvalidOutput };
        var output = encoded.ToArray();
        result = result with { CandidateBytes = output.Length };
        using var outputCodec = SKCodec.Create(encoded);
        if (outputCodec is null || outputCodec.Info.Width != width || outputCodec.Info.Height != height || outputCodec.EncodedFormat != SKEncodedImageFormat.Webp)
            return result with { SkipReason = WebpSkipReason.InvalidOutput };
        using var verified = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (outputCodec.GetPixels(verified.Info, verified.GetPixels()) != SKCodecResult.Success)
            return result with { SkipReason = WebpSkipReason.InvalidOutput };
        var expected = baseline.GetPixelSpan();
        var actual = verified.GetPixelSpan();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (expected[(y * baseline.RowBytes) + (x * 4) + 3] != actual[(y * verified.RowBytes) + (x * 4) + 3])
                return result with { SkipReason = WebpSkipReason.AlphaChanged };
            if (options.Lossless)
            {
                var expectedOffset = (y * baseline.RowBytes) + (x * 4);
                var actualOffset = (y * verified.RowBytes) + (x * 4);
                if (!expected.Slice(expectedOffset, 3).SequenceEqual(actual.Slice(actualOffset, 3)))
                    return result with { SkipReason = WebpSkipReason.PixelsChanged };
            }
        }
        var ssim = WindowedSsim(baseline, verified);
        result = result with { Ssim = ssim };
        if (enforceGates && ssim < options.MinimumSsim) return result with { SkipReason = WebpSkipReason.QualityBelowThreshold };
        var savings = source.Length - output.Length;
        if (enforceGates && (savings < options.MinimumSavingsBytes || (double)savings / source.Length < options.MinimumSavingsRatio))
            return result with { SkipReason = WebpSkipReason.InsufficientSavings };
        return result with { OutputBytes = output };
    }

    private static double WindowedSsim(SKBitmap expected, SKBitmap actual)
    {
        var a = expected.GetPixelSpan();
        var b = actual.GetPixelSpan();
        double total = 0;
        var windows = 0;
        const double c1 = 6.5025; // (0.01 * 255)^2
        const double c2 = 58.5225; // (0.03 * 255)^2
        for (var top = 0; top < expected.Height; top += 8)
        for (var left = 0; left < expected.Width; left += 8)
        {
            double sumA = 0, sumB = 0, squareA = 0, squareB = 0, product = 0;
            var count = 0;
            for (var y = top; y < Math.Min(top + 8, expected.Height); y++)
            for (var x = left; x < Math.Min(left + 8, expected.Width); x++)
            {
                var va = Luma(a, (y * expected.RowBytes) + (x * 4));
                var vb = Luma(b, (y * actual.RowBytes) + (x * 4));
                sumA += va; sumB += vb; squareA += va * va; squareB += vb * vb; product += va * vb; count++;
            }
            var meanA = sumA / count;
            var meanB = sumB / count;
            var varianceA = Math.Max(0, (squareA / count) - (meanA * meanA));
            var varianceB = Math.Max(0, (squareB / count) - (meanB * meanB));
            var covariance = (product / count) - (meanA * meanB);
            total += (((2 * meanA * meanB) + c1) * ((2 * covariance) + c2))
                / (((meanA * meanA) + (meanB * meanB) + c1) * (varianceA + varianceB + c2));
            windows++;
        }
        return Math.Clamp(total / windows, -1, 1);
    }

    private static double Luma(ReadOnlySpan<byte> pixels, int offset)
    {
        // Alpha is verified separately; invisible RGB values must not cause false quality failures.
        var alpha = pixels[offset + 3] / 255d;
        return (((0.2126 * pixels[offset]) + (0.7152 * pixels[offset + 1]) + (0.0722 * pixels[offset + 2])) * alpha) + (255 * (1 - alpha));
    }

    private static WebpSkipReason CheckStructure(ReadOnlySpan<byte> source)
    {
        if (source.Length >= 12 && source[..4].SequenceEqual("RIFF"u8) && source.Slice(8, 4).SequenceEqual("WEBP"u8))
            return CheckWebpStructure(source);
        if (source.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            var offset = 8;
            var hasData = false;
            while (offset <= source.Length - 12)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset, 4));
                if (length > int.MaxValue || length > source.Length - offset - 12) return WebpSkipReason.InvalidInput;
                var size = (int)length;
                var type = source.Slice(offset + 4, 4);
                var body = source.Slice(offset + 8, size);
                if (Crc(source.Slice(offset + 4, size + 4)) != BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset + 8 + size, 4)))
                    return WebpSkipReason.InvalidInput;
                if (type.SequenceEqual("acTL"u8)) return WebpSkipReason.AnimatedImage;
                if (type.SequenceEqual("iCCP"u8) || type.SequenceEqual("cICP"u8)) return WebpSkipReason.UnsupportedColorProfile;
                if (type.SequenceEqual("eXIf"u8)) return WebpSkipReason.UnsupportedOrientation;
                if (type.SequenceEqual("gAMA"u8) && (size != 4 || BinaryPrimitives.ReadUInt32BigEndian(body) != 45455)) return WebpSkipReason.UnsupportedColorProfile;
                if (type.SequenceEqual("cHRM"u8) && !body.SequenceEqual(new byte[] { 0,0,122,38, 0,0,128,132, 0,0,250,0, 0,0,128,232, 0,0,117,48, 0,0,234,96, 0,0,58,152, 0,0,23,112 })) return WebpSkipReason.UnsupportedColorProfile;
                if (type.SequenceEqual("IDAT"u8)) hasData = true;
                offset += size + 12;
                if (type.SequenceEqual("IEND"u8)) return size == 0 && hasData && offset == source.Length ? WebpSkipReason.None : WebpSkipReason.InvalidInput;
            }
            return WebpSkipReason.InvalidInput;
        }
        if (source.Length >= 2 && source[0] == 0xff && source[1] == 0xd8)
        {
            if (source.Length < 4 || source[^2] != 0xff || source[^1] != 0xd9) return WebpSkipReason.InvalidInput;
            var offset = 2;
            while (offset < source.Length - 2)
            {
                if (source[offset++] != 0xff) return WebpSkipReason.InvalidInput;
                while (offset < source.Length && source[offset] == 0xff) offset++;
                if (offset >= source.Length) return WebpSkipReason.InvalidInput;
                var marker = source[offset++];
                if (marker == 0xda) return JpegCompletenessValidator.Validate(source) ? WebpSkipReason.None : WebpSkipReason.InvalidInput;
                if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc0 or 0xc2 or 0xc4 or 0xc8 or 0xcc)) return WebpSkipReason.UnsupportedFormat;
                if (marker is 0xd8 or 0xd9 or 0x00) return WebpSkipReason.InvalidInput;
                if (marker is >= 0xd0 and <= 0xd7 || marker == 0x01) continue;
                if (offset > source.Length - 2) return WebpSkipReason.InvalidInput;
                var length = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset, 2));
                if (length < 2 || length > source.Length - offset) return WebpSkipReason.InvalidInput;
                var payload = source.Slice(offset + 2, length - 2);
                if (marker is 0xc0 or 0xc2 && payload.Length >= 6)
                {
                    var height = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(1, 2));
                    var width = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(3, 2));
                    if (width > MaximumDimension || height > MaximumDimension || (long)width * height > MaximumPixelCount)
                        return WebpSkipReason.InputLimitExceeded;
                }
                if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc) && payload.Length >= 6 && payload[5] == 4) return WebpSkipReason.UnsupportedColorProfile;
                if (marker == 0xe2 && payload.StartsWith("ICC_PROFILE\0"u8)) return WebpSkipReason.UnsupportedColorProfile;
                if (marker == 0xe2 && payload.StartsWith("MPF\0"u8)) return WebpSkipReason.AnimatedImage;
                offset += length;
            }
            return WebpSkipReason.InvalidInput;
        }
        return source.Length < 12 ? WebpSkipReason.InvalidInput : WebpSkipReason.UnsupportedFormat;
    }

    private static WebpSkipReason CheckWebpStructure(ReadOnlySpan<byte> source)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4)) != source.Length - 8)
            return WebpSkipReason.InvalidInput;
        var offset = 12;
        var imageSeen = false;
        var alphaSeen = false;
        var extendedSeen = false;
        var extendedFlags = 0;
        var canvasWidth = 0;
        var canvasHeight = 0;
        while (offset <= source.Length - 8)
        {
            var type = source.Slice(offset, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset + 4, 4));
            var paddedLength = (long)length + (length & 1);
            if (paddedLength > source.Length - offset - 8) return WebpSkipReason.InvalidInput;
            var payload = source.Slice(offset + 8, (int)length);
            if ((length & 1) != 0 && source[offset + 8 + (int)length] != 0) return WebpSkipReason.InvalidInput;
            if (type.SequenceEqual("ANIM"u8) || type.SequenceEqual("ANMF"u8)) return WebpSkipReason.AnimatedImage;
            if (type.SequenceEqual("ICCP"u8)) return WebpSkipReason.UnsupportedColorProfile;
            if (type.SequenceEqual("EXIF"u8) || type.SequenceEqual("XMP "u8)) return WebpSkipReason.UnsupportedOrientation;
            if (type.SequenceEqual("VP8X"u8))
            {
                if (offset != 12 || length != 10 || extendedSeen) return WebpSkipReason.InvalidInput;
                extendedSeen = true;
                extendedFlags = payload[0];
                if ((extendedFlags & 2) != 0) return WebpSkipReason.AnimatedImage;
                if ((extendedFlags & 32) != 0) return WebpSkipReason.UnsupportedColorProfile;
                if ((extendedFlags & 12) != 0) return WebpSkipReason.UnsupportedOrientation;
                if ((extendedFlags & 193) != 0 || payload[1] != 0 || payload[2] != 0 || payload[3] != 0) return WebpSkipReason.InvalidInput;
                canvasWidth = 1 + payload[4] + (payload[5] << 8) + (payload[6] << 16);
                canvasHeight = 1 + payload[7] + (payload[8] << 8) + (payload[9] << 16);
                if (canvasWidth > MaximumDimension || canvasHeight > MaximumDimension || (long)canvasWidth * canvasHeight > MaximumPixelCount)
                    return WebpSkipReason.InputLimitExceeded;
            }
            else if (type.SequenceEqual("ALPH"u8))
            {
                if (!extendedSeen || (extendedFlags & 16) == 0 || alphaSeen || imageSeen || length == 0)
                    return WebpSkipReason.InvalidInput;
                alphaSeen = true;
            }
            else if (type.SequenceEqual("VP8 "u8) || type.SequenceEqual("VP8L"u8))
            {
                if (imageSeen || length == 0) return WebpSkipReason.InvalidInput;
                imageSeen = true;
                int width, height;
                if (type.SequenceEqual("VP8L"u8))
                {
                    if (length < 5 || payload[0] != 47 || alphaSeen) return WebpSkipReason.InvalidInput;
                    var bits = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4));
                    if ((bits >> 29) != 0) return WebpSkipReason.InvalidInput;
                    width = (int)(bits & 16383) + 1;
                    height = (int)((bits >> 14) & 16383) + 1;
                }
                else
                {
                    if (length < 10 || !payload.Slice(3, 3).SequenceEqual(new byte[] { 157, 1, 42 }))
                        return WebpSkipReason.InvalidInput;
                    width = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2)) & 16383;
                    height = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2)) & 16383;
                    if (extendedSeen && ((extendedFlags & 16) != 0) != alphaSeen) return WebpSkipReason.InvalidInput;
                }
                if (width == 0 || height == 0 || (extendedSeen && (width != canvasWidth || height != canvasHeight)))
                    return WebpSkipReason.InvalidInput;
                if (width > MaximumDimension || height > MaximumDimension || (long)width * height > MaximumPixelCount)
                    return WebpSkipReason.InputLimitExceeded;
            }
            offset += 8 + (int)paddedLength;
        }
        return imageSeen && offset == source.Length ? WebpSkipReason.None : WebpSkipReason.InvalidInput;
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return crc ^ uint.MaxValue;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320 ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
