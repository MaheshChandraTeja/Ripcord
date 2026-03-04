namespace Ripcord.Receipts.Model
{
    public sealed class ReceiptStats
    {
        public long FilesProcessed { get; set; }
        public long BytesProcessed { get; set; }
        public long FilesSucceeded { get; set; }
        public long FilesFailed { get; set; }
    }
}
