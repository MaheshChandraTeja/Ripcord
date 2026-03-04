#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ripcord.Receipts.Model
{
    /// <summary>
    /// A complete, immutable record of a shred job. Designed for long-term audit storage.
    /// </summary>
    public sealed record Receipt(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("jobId")] Guid JobId,
        [property: JsonPropertyName("createdUtc")] DateTimeOffset CreatedUtc,
        [property: JsonPropertyName("startedUtc")] DateTimeOffset StartedUtc,
        [property: JsonPropertyName("completedUtc")] DateTimeOffset CompletedUtc,
        [property: JsonPropertyName("machine")] string Machine,
        [property: JsonPropertyName("user")] string User,
        [property: JsonPropertyName("profileId")] string ProfileId,
        [property: JsonPropertyName("profileName")] string ProfileName,
        [property: JsonPropertyName("dryRun")] bool DryRun,
        [property: JsonPropertyName("stats")] ReceiptStats Stats,
        [property: JsonPropertyName("items")] IReadOnlyList<ReceiptItem> Items,
        [property: JsonPropertyName("journalFiles")] IReadOnlyList<string>? JournalFiles = null,
        [property: JsonPropertyName("freeSpace")] FreeSpaceSummary? FreeSpace = null,
        [property: JsonPropertyName("notes")] IReadOnlyList<string>? Notes = null
    )
    {
        public static Receipt Create(
            Guid jobId,
            DateTimeOffset startedUtc,
            DateTimeOffset completedUtc,
            string machine,
            string user,
            string profileId,
            string profileName,
            bool dryRun,
            IReadOnlyList<ReceiptItem> items,
            ReceiptStats? stats = null,
            IReadOnlyList<string>? journalFiles = null,
            FreeSpaceSummary? freeSpace = null,
            IReadOnlyList<string>? notes = null) =>
            new(
                SchemaVersion: "1.0",
                JobId: jobId,
                CreatedUtc: DateTimeOffset.UtcNow,
                StartedUtc: startedUtc,
                CompletedUtc: completedUtc,
                Machine: machine,
                User: user,
                ProfileId: profileId,
                ProfileName: profileName,
                DryRun: dryRun,
                Stats: stats ?? ReceiptStats.From(items),
                Items: items,
                JournalFiles: journalFiles,
                FreeSpace: freeSpace,
                Notes: notes
            ).Validate();

        public Receipt Validate()
        {
            if (string.IsNullOrWhiteSpace(SchemaVersion)) throw new ArgumentException("SchemaVersion required");
            if (JobId == Guid.Empty) throw new ArgumentException("JobId required");
            if (string.IsNullOrWhiteSpace(Machine)) throw new ArgumentException("Machine required");
            if (string.IsNullOrWhiteSpace(User)) throw new ArgumentException("User required");
            if (string.IsNullOrWhiteSpace(ProfileId)) throw new ArgumentException("ProfileId required");
            if (string.IsNullOrWhiteSpace(ProfileName)) throw new ArgumentException("ProfileName required");
            if (Items is null || Items.Count == 0) throw new ArgumentException("Items required");
            foreach (var it in Items) it.Validate();
            return this;
        }

        /// <summary>Deterministic UTF-8 canonical JSON for hashing & signing.</summary>
        public byte[] ToCanonicalJsonUtf8() => Canon.Canonicalize(this);

        /// <summary>SHA-256 over canonical JSON (hex).</summary>
        public string ComputeHashHex()
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(ToCanonicalJsonUtf8());
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) _ = sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // ---------- nested models ----------

        public sealed record ReceiptItem(
            [property: JsonPropertyName("path")] string Path,
            [property: JsonPropertyName("sizeBytes")] long SizeBytes,
            [property: JsonPropertyName("deleted")] bool Deleted,
            [property: JsonPropertyName("passes")] int Passes,
            [property: JsonPropertyName("adsBefore")] int AdsBefore,
            [property: JsonPropertyName("adsAfter")] int AdsAfter,
            [property: JsonPropertyName("verificationOk")] bool? VerificationOk = null,
            [property: JsonPropertyName("verificationSampleFraction")] double? VerificationSampleFraction = null,
            [property: JsonPropertyName("ranges")] IReadOnlyList<ErasedRange>? Ranges = null,
            [property: JsonPropertyName("shadow")] ShadowInfo? Shadow = null,
            [property: JsonPropertyName("notes")] IReadOnlyList<string>? Notes = null
        )
        {
            public ReceiptItem Validate()
            {
                if (string.IsNullOrWhiteSpace(Path)) throw new ArgumentException("Path required");
                if (SizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(SizeBytes));
                if (Passes < 0) throw new ArgumentOutOfRangeException(nameof(Passes));
                if (AdsBefore < -1 || AdsAfter < -1) throw new ArgumentOutOfRangeException("ADS counts must be >= -1");
                if (Ranges is not null) foreach (var r in Ranges) r.Validate();
                return this;
            }
        }

        public sealed record ReceiptStats(
            [property: JsonPropertyName("filesProcessed")] int FilesProcessed,
            [property: JsonPropertyName("filesFailed")] int FilesFailed,
            [property: JsonPropertyName("filesDeleted")] int FilesDeleted,
            [property: JsonPropertyName("bytesOverwritten")] long BytesOverwritten
        )
        {
            public static ReceiptStats From(IReadOnlyList<ReceiptItem> items)
            {
                int processed = items.Count;
                int deleted = items.Count(i => i.Deleted);
                long bytes = items.Sum(i => i.SizeBytes);
                return new ReceiptStats(processed, filesFailed: 0, filesDeleted: deleted, bytesOverwritten: bytes);
            }
        }

        public sealed record FreeSpaceSummary(
            [property: JsonPropertyName("volume")] string VolumeRoot,
            [property: JsonPropertyName("bytesWritten")] long BytesWritten,
            [property: JsonPropertyName("filesCreated")] int FilesCreated,
            [property: JsonPropertyName("trimAttempted")] bool TrimAttempted
        );
    }

    // ========= Deterministic JSON (canonicalization) =========
    internal static class Canon
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
            Converters = { new DateTimeOffsetConverter() }
        };

        public static byte[] Canonicalize<T>(T value)
        {
            var element = JsonSerializer.SerializeToElement(value!, Options);
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });

            WriteElementCanonical(writer, element);
            writer.Flush();
            return ms.ToArray();
        }

        private static void WriteElementCanonical(Utf8JsonWriter w, JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    w.WriteStartObject();
                    var props = e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal);
                    foreach (var p in props)
                    {
                        w.WritePropertyName(p.Name);
                        WriteElementCanonical(w, p.Value);
                    }
                    w.WriteEndObject();
                    break;

                case JsonValueKind.Array:
                    w.WriteStartArray();
                    foreach (var item in e.EnumerateArray())
                        WriteElementCanonical(w, item);
                    w.WriteEndArray();
                    break;

                case JsonValueKind.String:
                    w.WriteStringValue(e.GetString());
                    break;

                case JsonValueKind.Number:
                    // Preserve numeric text as parsed (write double or long depending).
                    if (e.TryGetInt64(out var l)) w.WriteNumberValue(l);
                    else if (e.TryGetUInt64(out var ul)) w.WriteNumberValue(ul);
                    else w.WriteNumberValue(e.GetDouble());
                    break;

                case JsonValueKind.True: w.WriteBooleanValue(true); break;
                case JsonValueKind.False: w.WriteBooleanValue(false); break;
                case JsonValueKind.Null: w.WriteNullValue(); break;

                default: throw new NotSupportedException($"Unsupported JSON kind: {e.ValueKind}");
            }
        }

        private sealed class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
        {
            public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                => DateTimeOffset.Parse(reader.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind);
            public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.ToUniversalTime().ToString("O"));
        }
    }
}
