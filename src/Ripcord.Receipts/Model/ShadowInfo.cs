#nullable enable
using System.Text.Json.Serialization;

namespace Ripcord.Receipts.Model
{
    /// <summary>
    /// Snapshot metadata captured for evidence (e.g., when mapping a file into a VSS snapshot).
    /// </summary>
    public sealed record ShadowInfo(
        [property: JsonPropertyName("snapshotId")] Guid SnapshotId,
        [property: JsonPropertyName("volume")] string VolumeName,
        [property: JsonPropertyName("deviceObject")] string DeviceObject,
        [property: JsonPropertyName("createdUtc")] DateTimeOffset? CreatedUtc,
        [property: JsonPropertyName("clientAccessible")] bool ClientAccessible,
        [property: JsonPropertyName("persistent")] bool Persistent,
        [property: JsonPropertyName("mappedPath")] string? MappedPath = null
    );
}
