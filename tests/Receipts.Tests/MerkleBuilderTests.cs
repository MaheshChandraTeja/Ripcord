#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Ripcord.Receipts.Merkle;
using Xunit;

namespace Receipts.Tests
{
    public sealed class MerkleBuilderTests
    {
        [Fact]
        public void Root_OddLeaf_DuplicatesLast()
        {
            var mb = new MerkleBuilder()
                .AddLeaf("a")
                .AddLeaf("b")
                .AddLeaf("c"); // odd → duplicate c

            var root = mb.Build().ToArray();
            Assert.Equal(32, root.Length);

            // Verify by recomputing from proof for index 1
            var proof = mb.GetProof(1);
            bool ok = MerkleBuilder.VerifyProof(Encoding.UTF8.GetBytes("b"), root, proof);
            Assert.True(ok);
        }

        [Fact]
        public void Proof_Verifies_ForAllLeaves()
        {
            var data = new[] { "alpha", "beta", "gamma", "delta", "epsilon" };
            var mb = new MerkleBuilder();
            foreach (var s in data) mb.AddLeaf(s);

            var root = mb.Build().ToArray();
            for (int i = 0; i < data.Length; i++)
            {
                var pr = mb.GetProof(i);
                Assert.True(MerkleBuilder.VerifyProof(Encoding.UTF8.GetBytes(data[i]), root, pr), $"Proof failed for index {i}");
            }
        }
    }
}
