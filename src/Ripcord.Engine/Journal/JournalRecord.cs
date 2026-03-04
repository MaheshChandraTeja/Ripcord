
// =============================
// File: src/Ripcord.Engine/Journal/JournalRecord.cs
// =============================
using System;
using System.Text.Json.Serialization;

namespace Ripcord.Engine.Journal
{
    public enum JournalLevel { Debug, Info, Warn, Error }

    /// <summary>
    /// Structured, append-only record for shred operations.
    /// </summary>
    public sealed record JournalRecord(
        DateTimeOffset Timestamp,
        JournalLevel Level,
        string Event,
        string? TargetPath = null,
        string? ProfileId = null,
        int? PassIndex = null,
        long? BytesProcessed = null,
        string? HashBefore = null,
        string? HashAfter = null,
        int? AdsCountBefore = null,
        int? AdsCountAfter = null,
        bool? Success = null,
        string? Message = null,
        string? Exception = null)
    {
        [JsonIgnore]
        public bool IsError => Level == JournalLevel.Error;
    }
}
