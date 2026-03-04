#nullable enable
using System.Text.Json.Serialization;

namespace Ripcord.Receipts.Model
{
    /// <summary>
    /// Describes an overwritten byte range within a file for evidentiary receipts.
    /// </summary>
    public enum ErasePatternType
    {
        Unknown = 0,
        Constant = 1,
        Complement = 2,
        Random = 3
    }

    /// <summary>Represents a contiguous range that was overwritten.</summary>
    public sealed record ErasedRange(
        [property: JsonPropertyName("offset")] long Offset,
        [property: JsonPropertyName("length")] long Length,
        [property: JsonPropertyName("pattern")] ErasePatternType Pattern,
        [property: JsonPropertyName("constantByte")] byte? ConstantByte = null
    )
    {
        public ErasedRange Validate()
        {
            if (Offset < 0) throw new ArgumentOutOfRangeException(nameof(Offset));
            if (Length <= 0) throw new ArgumentOutOfRangeException(nameof(Length));
            if ((Pattern == ErasePatternType.Constant || Pattern == ErasePatternType.Complement) && ConstantByte is null)
                throw new ArgumentException("ConstantByte is required for Constant/Complement patterns.", nameof(ConstantByte));
            return this;
        }
    }
}
