namespace Jellyfin.Plugin.JellyP.Conversion;

/// <summary>Bounded settings for a single candidate, with no source file mutation.</summary>
public sealed record WebpOptimizationOptions
{
    /// <summary>WebP quality from 0 through 100; in lossless mode this controls compression effort.</summary>
    public int Quality { get; init; } = 85;
    /// <summary>Require exact decoded RGBA samples against the resized baseline. Candidates that
    /// normalize hidden RGB in fully transparent pixels are rejected rather than called lossless.</summary>
    public bool Lossless { get; init; }
    /// <summary>Maximum output edge; smaller sources are never enlarged.</summary>
    public int MaximumDimension { get; init; } = 8192;
    /// <summary>Minimum mean 8 by 8 window luma SSIM against the resized source.</summary>
    public double MinimumSsim { get; init; } = 0.96;
    /// <summary>Minimum fraction of encoded source bytes saved.</summary>
    public double MinimumSavingsRatio { get; init; } = 0.05;
    /// <summary>Minimum absolute number of encoded bytes saved.</summary>
    public int MinimumSavingsBytes { get; init; } = 4096;
}

/// <summary>Why a candidate was conservatively rejected.</summary>
public enum WebpSkipReason
{
    /// <summary>The candidate passed all gates.</summary>
    None,
    /// <summary>Input is empty, malformed or incompletely decoded.</summary>
    InvalidInput,
    /// <summary>Input bytes or decoded dimensions exceed the hard limits.</summary>
    InputLimitExceeded,
    /// <summary>Only supported static Huffman JPEG, PNG and WebP sources are converted.</summary>
    UnsupportedFormat,
    /// <summary>Animated or multi-picture sources are not converted.</summary>
    AnimatedImage,
    /// <summary>Source orientation cannot safely be discarded.</summary>
    UnsupportedOrientation,
    /// <summary>Source color metadata cannot safely be discarded.</summary>
    UnsupportedColorProfile,
    /// <summary>Encoding or verification of the output failed.</summary>
    InvalidOutput,
    /// <summary>Any decoded alpha sample changed.</summary>
    AlphaChanged,
    /// <summary>Lossless encoding changed at least one RGB sample, including invisible RGB.</summary>
    PixelsChanged,
    /// <summary>The decoded candidate did not reach the SSIM threshold.</summary>
    QualityBelowThreshold,
    /// <summary>The candidate did not meet both savings thresholds.</summary>
    InsufficientSavings,
}

/// <summary>Candidate evidence. Output bytes are exposed only after all gates pass.</summary>
public sealed record WebpOptimizationResult
{
    /// <summary>Whether the candidate is safe to pass to the publication layer.</summary>
    public bool Success => SkipReason == WebpSkipReason.None && OutputBytes is not null;
    /// <summary>The gate that rejected the candidate, or None on success.</summary>
    public WebpSkipReason SkipReason { get; init; }
    /// <summary>Verified encoded output, present only on success.</summary>
    public byte[]? OutputBytes { get; init; }
    /// <summary>Encoded input size.</summary>
    public int SourceBytes { get; init; }
    /// <summary>Encoded candidate size, including rejected candidates if encoding succeeded.</summary>
    public int CandidateBytes { get; init; }
    /// <summary>Decoded source width, if available.</summary>
    public int SourceWidth { get; init; }
    /// <summary>Decoded source height, if available.</summary>
    public int SourceHeight { get; init; }
    /// <summary>Candidate width, if available.</summary>
    public int OutputWidth { get; init; }
    /// <summary>Candidate height, if available.</summary>
    public int OutputHeight { get; init; }
    /// <summary>Mean windowed luma SSIM, if the comparison was performed.</summary>
    public double? Ssim { get; init; }
}
