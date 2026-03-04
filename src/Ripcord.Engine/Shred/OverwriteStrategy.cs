using System;

namespace Ripcord.Engine.Shred
{
    /// <summary>
    /// Buffer fill strategy for a given overwrite pass.
    /// </summary>
    public static class OverwriteStrategy
    {
        /// <summary>
        /// Fills <paramref name="dest"/> with bytes according to <paramref name="pass"/>.
        /// Returns number of bytes filled (equals dest.Length).
        /// </summary>
        public static int Fill(OverwritePass pass, Span<byte> dest, Random? random = null)
        {
            switch (pass.PatternType)
            {
                case OverwritePatternType.Constant:
                    dest.Fill(pass.Constant);
                    return dest.Length;

                case OverwritePatternType.Complement:
                    dest.Fill((byte)~pass.Constant);
                    return dest.Length;

                case OverwritePatternType.Random:
                    (random ?? Random.Shared).NextBytes(dest);
                    return dest.Length;

                default:
                    throw new ArgumentOutOfRangeException(nameof(pass.PatternType));
            }
        }
    }
}
