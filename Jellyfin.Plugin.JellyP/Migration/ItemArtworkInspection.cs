#pragma warning disable CS1591 // Path-free administrator inspection DTOs.
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyP.Migration;

/// <summary>A read-only view of images registered on one exact item, before shared-reference checks.</summary>
public sealed record ItemArtworkInspection(Guid ItemId, string? Name, string? ItemType, int? ProductionYear,
    bool Found, IReadOnlyList<ArtworkImageInspection> Images);

/// <summary>One registered image slot; Ready means preliminary candidate, not verified conversion.</summary>
public sealed record ArtworkImageInspection(ImageType Type, int Index, string Category, string State);
