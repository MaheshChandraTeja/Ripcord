#nullable enable
using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Ripcord.Receipts.Merkle
{
    /// <summary>
    /// Deterministic SHA-256 Merkle tree with domain separation:
    ///   leaf:  0x00 || data
    ///   node:  0x01 || leftHash || rightHash
    /// Odd nodes are duplicated (Bitcoin style). All APIs are thread-safe if you do not mutate after Build().
    /// </summary>
    public sealed class MerkleBuilder
    {
        private byte[][]? _leaves;
        private byte[][]? _level0;
        private byte[]? _root;

        public int LeafCount => _leaves?.Length ?? 0;
        public bool IsBuilt => _root is not null;
        public string RootHex => _root is null ? string.Empty : Convert.ToHexString(_root).ToLowerInvariant();

        public MerkleBuilder AddLeaf(ReadOnlySpan<byte> data)
        {
            var hash = HashLeaf(data);
            if (_leaves is null) _leaves = Array.Empty<byte[]>();
            Array.Resize(ref _leaves, _leaves.Length + 1);
            _leaves[^1] = hash;
            _root = null;
            return this;
        }

        public MerkleBuilder AddLeaf(string textUtf8)
            => AddLeaf(Encoding.UTF8.GetBytes(textUtf8));

        public MerkleBuilder AddLeafHashed(ReadOnlySpan<byte> leafHash)
        {
            // Accept a prehashed leaf (must already be HashLeaf(data)).
            var copy = leafHash.ToArray();
            if (_leaves is null) _leaves = Array.Empty<byte[]>();
            Array.Resize(ref _leaves, _leaves.Length + 1);
            _leaves[^1] = copy;
            _root = null;
            return this;
        }

        /// <summary>Builds the tree (idempotent). Returns the 32-byte root.</summary>
        public ReadOnlySpan<byte> Build()
        {
            if (_leaves is null || _leaves.Length == 0)
                throw new InvalidOperationException("No leaves added.");

            if (_root is not null) return _root;

            // First level is the leaf hashes themselves.
            _level0 = new byte[_leaves.Length][];
            for (int i = 0; i < _leaves.Length; i++) _level0[i] = _leaves[i];

            var level = _level0;
            while (level!.Length > 1)
            {
                int nextCount = (level.Length + 1) / 2;
                var next = new byte[nextCount][];
                for (int i = 0, j = 0; i < level.Length; i += 2, j++)
                {
                    var left = level[i];
                    var right = (i + 1 < level.Length) ? level[i + 1] : left; // duplicate last if odd
                    next[j] = HashNode(left, right);
                }
                level = next;
            }

            _root = level[0];
            return _root;
        }

        /// <summary>
        /// Returns a Merkle audit proof for a leaf index. Build() is called automatically.
        /// </summary>
        public MerkleProof GetProof(int leafIndex)
        {
            if (_leaves is null) throw new InvalidOperationException("No leaves.");
            if (leafIndex < 0 || leafIndex >= _leaves.Length) throw new ArgumentOutOfRangeException(nameof(leafIndex));
            Build();

            // Walk levels built on the fly to get sibling at each height
            var level = _level0!;
            int idx = leafIndex;
            var proof = new MerkleProof();

            while (level.Length > 1)
            {
                bool hasRight = (idx % 2 == 0) && (idx + 1 < level.Length);
                bool isRightNode = (idx % 2 == 1);

                byte[] left = level[idx - (isRightNode ? 1 : 0)];
                byte[] right = hasRight ? level[idx + 1] : level[idx - (isRightNode ? 1 : 0)];

                // The sibling is whichever is not at idx
                var sibling = isRightNode ? left : right;
                proof.AddSibling(sibling, isRightNode ? SiblingSide.Left : SiblingSide.Right);

                // Build next level to move up
                int nextCount = (level.Length + 1) / 2;
                var next = new byte[nextCount][];
                for (int i = 0, j = 0; i < level.Length; i += 2, j++)
                {
                    var l = level[i];
                    var r = (i + 1 < level.Length) ? level[i + 1] : l;
                    next[j] = HashNode(l, r);
                }

                idx /= 2;
                level = next;
            }

            return proof;
        }

        public static bool VerifyProof(ReadOnlySpan<byte> leafData, ReadOnlySpan<byte> root, MerkleProof proof)
        {
            var h = HashLeaf(leafData);
            return VerifyProofHashed(h, root, proof);
        }

        public static bool VerifyProofHashed(ReadOnlySpan<byte> leafHash, ReadOnlySpan<byte> root, MerkleProof proof)
        {
            var acc = leafHash.ToArray();
            foreach (var step in proof.Steps)
            {
                acc = step.Side == SiblingSide.Left
                    ? HashNode(step.Hash, acc)
                    : HashNode(acc, step.Hash);
            }
            return CryptographicOperations.FixedTimeEquals(acc, root);
        }

        // ---- hashing primitives ----
        private static byte[] HashLeaf(ReadOnlySpan<byte> data)
        {
            Span<byte> tmp = stackalloc byte[1 + data.Length];
            tmp[0] = 0x00;
            data.CopyTo(tmp[1..]);
            return SHA256.HashData(tmp);
        }

        private static byte[] HashNode(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            byte[] tmp = ArrayPool<byte>.Shared.Rent(1 + left.Length + right.Length);
            try
            {
                tmp[0] = 0x01;
                left.CopyTo(tmp.AsSpan(1));
                right.CopyTo(tmp.AsSpan(1 + left.Length));
                return SHA256.HashData(tmp.AsSpan(0, 1 + left.Length + right.Length));
            }
            finally { ArrayPool<byte>.Shared.Return(tmp); }
        }
    }

    public enum SiblingSide { Left = 0, Right = 1 }

    public sealed class MerkleProof
    {
        public readonly record struct Step(byte[] Hash, SiblingSide Side);
        private readonly System.Collections.Generic.List<Step> _steps = new();
        public System.Collections.Generic.IReadOnlyList<Step> Steps => _steps;

        internal void AddSibling(byte[] hash, SiblingSide side) => _steps.Add(new Step(hash, side));
    }
}
