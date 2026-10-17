using Jellyfin.Plugin.JellyP.Conversion;
using Xunit;

namespace Jellyfin.Plugin.JellyP.Tests;

public sealed class WebpProgressiveJpegTests
{
    private readonly WebpImageConverter _converter = new();
    private static WebpOptimizationOptions Options => new() { MinimumSsim = 0, MinimumSavingsBytes = 0, MinimumSavingsRatio = 0 };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Optimize_ProgressiveDcAcRefinementEobAndRestartsAreDecoded(bool restarts)
    {
        // Two grayscale blocks: one preexisting AC coefficient, one newly introduced
        // during refinement, correction bits, EOB runs, and optional restart resets.
        var result = _converter.Optimize(CreateProgressive(restarts), Options);
        Assert.True(result.Success, result.SkipReason.ToString());
        Assert.Equal((16, 8), (result.SourceWidth, result.SourceHeight));
        Assert.NotNull(result.OutputBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Optimize_RejectsProgressiveScanTruncationDespiteForgedEoi(bool restarts)
    {
        var source = CreateProgressive(restarts);
        var scans = FindScans(source);
        Assert.Equal(4, scans.Count);
        foreach (var scan in scans)
        {
            var cut = scan.Start + ((scan.End - scan.Start) / 2);
            var truncated = source[..cut].Concat(new byte[] { 255, 217 }).ToArray();
            Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(truncated, Options).SkipReason);
        }
    }

    [Fact]
    public void Optimize_AcceptsCompletedProgressiveScanBoundaryAsValidLowerDetailImage()
    {
        var source = CreateProgressive(false);
        var firstScan = FindScans(source)[0];
        var lowerDetail = source[..firstScan.End].Concat(new byte[] { 255, 217 }).ToArray();
        var result = _converter.Optimize(lowerDetail, Options);
        Assert.True(result.Success, result.SkipReason.ToString());
    }

    [Fact]
    public void Optimize_RejectsEobRunPastScanOrRestartAndIllegalRefinement()
    {
        var source = CreateProgressive(false);
        var scans = FindScans(source);
        // Initial AC EOB is encoded as run2 + appended0. Appended1 means run3 for only two blocks.
        source[scans[1].Start] |= 1;
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(source, Options).SkipReason);
        source = CreateProgressive(true);
        scans = FindScans(source);
        // Code001 (EOB run2) crosses the restart boundary, which arrives after one block.
        source[scans[1].Start] = 0x2f;
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(source, Options).SkipReason);
        source = CreateProgressive(false);
        scans = FindScans(source);
        source[scans[2].Start - 1] = 0x20; // Ah2 cannot refine the preceding Al1 scan.
        Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(source, Options).SkipReason);
        source = CreateProgressive(false);
        var sof = source.AsSpan().IndexOf(new byte[] { 255, 194 });
        source[sof + 7] = 0x20; source[sof + 8] = 1; // Width8193: reject before occupancy allocation.
        Assert.Equal(WebpSkipReason.InputLimitExceeded, _converter.Optimize(source, Options).SkipReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Optimize_ProgressiveRefinementConsumesEobAndZrlCorrectionBits(bool zrl)
    {
        var source = CreateProgressive(false);
        var refinement = FindScans(source)[3];
        // EOB run2 followed by a correction bit for the existing coefficient, or
        // ZRL across 16 zero coefficients while correcting an existing nonzero,
        // then introduction of a new coefficient and EOB run2.
        byte[] entropy = zrl ? [113, 47] : [47];
        source = source[..refinement.Start].Concat(entropy).Concat(source[refinement.End..]).ToArray();
        var result = _converter.Optimize(source, Options);
        Assert.True(result.Success, result.SkipReason.ToString());
        if (zrl)
        {
            source[refinement.Start - 2] = 16; // This band cannot contain all16 skipped zeroes.
            Assert.Equal(WebpSkipReason.InvalidInput, _converter.Optimize(source, Options).SkipReason);
        }
    }

    private static byte[] CreateProgressive(bool restarts)
    {
        var image = new List<byte> { 255, 216 };
        AddSegment(image, 0xdb, new byte[] { 0 }.Concat(Enumerable.Repeat((byte)1, 64)).ToArray());
        AddSegment(image, 0xc2, [8, 0, 8, 0, 16, 1, 1, 17, 0]);
        AddSegment(image, 0xc4, new byte[] { 0, 1 }.Concat(new byte[15]).Concat(new byte[] { 0 }).ToArray());
        // Four three-bit AC codes: 000=size1, 001=EOB run2, 010=EOB run1, 011=ZRL.
        AddSegment(image, 0xc4, new byte[] { 16, 0, 0, 4 }.Concat(new byte[13]).Concat(new byte[] { 1, 16, 0, 240 }).ToArray());
        if (restarts) AddSegment(image, 0xdd, [0, 1]);
        AddScan(image, 0, 0, 0, 1, restarts ? [127, 255, 208, 127] : [63]);
        AddScan(image, 1, 63, 0, 1, restarts ? [21, 255, 208, 95] : [18]);
        AddScan(image, 0, 0, 1, 0, restarts ? [255, 0, 255, 208, 127] : [191]);
        AddScan(image, 1, 63, 1, 0, restarts ? [10, 255, 208, 95] : [9, 127]);
        image.AddRange([255, 217]);
        return image.ToArray();
    }

    private static void AddScan(List<byte> image, byte start, byte end, byte ah, byte al, byte[] entropy)
    {
        AddSegment(image, 0xda, [1, 1, 0, start, end, (byte)((ah << 4) | al)]);
        image.AddRange(entropy);
    }

    private static void AddSegment(List<byte> image, byte marker, byte[] payload)
    {
        image.AddRange([255, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2)]);
        image.AddRange(payload);
    }

    private static List<(int Start, int End)> FindScans(byte[] source)
    {
        var scans = new List<(int, int)>();
        var offset = 2;
        while (offset < source.Length - 2)
        {
            Assert.Equal(255, source[offset++]);
            var marker = source[offset++];
            if (marker == 217) break;
            var size = (source[offset] << 8) | source[offset + 1];
            offset += size;
            if (marker != 218) continue;
            var start = offset;
            while (offset < source.Length - 1)
            {
                if (source[offset] != 255) { offset++; continue; }
                if (source[offset + 1] == 0 || source[offset + 1] is >= 208 and <= 215) { offset += 2; continue; }
                break;
            }
            scans.Add((start, offset));
        }
        return scans;
    }
}
