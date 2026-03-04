#nullable enable
namespace Ripcord.Receipts.Zk
{
    /// <summary>
    /// Placeholder for an optional zero-knowledge range proof (e.g., value ∈ [min,max]).
    /// This stub lets you wire the API without bringing in heavy crypto dependencies.
    /// </summary>
    public static class RangeProof
    {
        public sealed record Proof(byte[] Commitment, byte[]? ProofBytes, string Scheme);

        public static bool IsAvailable => false; // set true when a provider is injected

        public static Proof Create(ulong value, ulong minInclusive, ulong maxInclusive)
            => throw new System.NotSupportedException("RangeProof is not enabled in this build.");

        public static bool Verify(Proof proof, ulong minInclusive, ulong maxInclusive)
            => throw new System.NotSupportedException("RangeProof verification is not enabled in this build.");
    }
}
