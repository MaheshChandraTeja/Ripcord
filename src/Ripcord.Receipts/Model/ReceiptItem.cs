namespace Ripcord.Receipts.Model
{
    public sealed class ReceiptItem
    {
        public string? Path { get; set; }
        public long SizeBytes { get; set; }
        public string? Algorithm { get; set; }   // e.g., "DoD 5220.22-M", "NIST 800-88"
        public bool Success { get; set; }
        public string? Note { get; set; }
    }
}
