namespace Ripcord.Receipts.Model
{
    public sealed class FreeSpaceSummary
    {
        public string? Volume { get; set; }      // "C:\"
        public long TrimmedBytes { get; set; }   // for SSD TRIM/Free-space wipe
        public long Errors { get; set; }
    }
}
