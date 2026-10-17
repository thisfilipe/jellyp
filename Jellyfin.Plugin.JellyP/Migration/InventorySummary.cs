#pragma warning disable CS1591 // Aggregate administration DTO, without artwork paths.
namespace Jellyfin.Plugin.JellyP.Migration;

/// <summary>Explains an empty inventory without exposing the server filesystem.</summary>
public sealed class InventorySummary
{
    public int TotalItems { get; set; }
    public int MatchedItems { get; set; }
    public int LocalImages { get; set; }
    public int EligibleImages { get; set; }
    public int AlreadyWebpImages { get; set; }
    public int SharedImages { get; set; }
    public int ExcludedImages { get; set; }
}
