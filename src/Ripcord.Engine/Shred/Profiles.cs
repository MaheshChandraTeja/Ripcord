// =============================
// File: src/Ripcord.Engine/Shred/Profiles.cs
// =============================
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ripcord.Engine.Shred
{
    /// <summary>
    /// Type of overwrite pattern for a pass.
    /// </summary>
    public enum OverwritePatternType
    {
        Constant = 0,
        Random = 1,
        Complement = 2
    }

    /// <summary>
    /// Describes a single overwrite pass.
    /// </summary>
    public readonly struct OverwritePass
    {
        public string Name { get; }
        public OverwritePatternType PatternType { get; }
        public byte Constant { get; }

        public OverwritePass(string name, OverwritePatternType type, byte constant = 0x00)
        {
            Name = name;
            PatternType = type;
            Constant = constant;
        }

        public override string ToString() => $"{Name} ({PatternType}{(PatternType == OverwritePatternType.Constant ? $", 0x{Constant:X2}" : string.Empty)})";
    }

    /// <summary>
    /// Contract for a shred profile.
    /// </summary>
    public interface IShredProfile
    {
        string Id { get; }
        string DisplayName { get; }
        IReadOnlyList<OverwritePass> Passes { get; }

        /// <summary>Remove and shred Alternate Data Streams.</summary>
        bool WipeAlternateDataStreams { get; }

        /// <summary>Attempt to reduce forensic filename residue by repeated renames.</summary>
        bool ApplyRenameNoise { get; }
        int RenameIterations { get; }

        /// <summary>
        /// Attempt to reduce MFT slack artifacts (best-effort, see MftSlackCleaner for details).
        /// </summary>
        bool WipeMftSlack { get; }
    }

    /// <summary>
    /// Base class for immutable shred profiles.
    /// </summary>
    public abstract class ShredProfileBase : IShredProfile
    {
        protected ShredProfileBase(string id, string displayName, IEnumerable<OverwritePass> passes,
            bool wipeAds = true, bool renameNoise = true, int renameIterations = 6, bool wipeMftSlack = false)
        {
            Id = id;
            DisplayName = displayName;
            Passes = passes.ToArray();
            WipeAlternateDataStreams = wipeAds;
            ApplyRenameNoise = renameNoise;
            RenameIterations = Math.Clamp(renameIterations, 0, 64);
            WipeMftSlack = wipeMftSlack;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<OverwritePass> Passes { get; }
        public bool WipeAlternateDataStreams { get; }
        public bool ApplyRenameNoise { get; }
        public int RenameIterations { get; }
        public bool WipeMftSlack { get; }
    }

    /// <summary>
    /// Single pass of 0x00. Fast and suitable for non-sensitive data on modern drives.
    /// </summary>
    public sealed class ZeroFillProfile : ShredProfileBase
    {
        public ZeroFillProfile() : base(
            id: "zero",
            displayName: "Zero Fill (1 pass)",
            passes: new[] { new OverwritePass("Zeros", OverwritePatternType.Constant, 0x00) },
            wipeAds: true, renameNoise: true, renameIterations: 4, wipeMftSlack: false)
        { }
    }

    /// <summary>
    /// NIST 800-88 Rev. 1 Clear (1 pass random), with verify and metadata hygiene.
    /// </summary>
    public sealed class NistClearProfile : ShredProfileBase
    {
        public NistClearProfile() : base(
            id: "nist-clear",
            displayName: "NIST 800-88 Clear (1 random)",
            passes: new[] { new OverwritePass("Random", OverwritePatternType.Random) },
            wipeAds: true, renameNoise: true, renameIterations: 6, wipeMftSlack: false)
        { }
    }

    /// <summary>
    /// DoD 5220.22-M (3 passes: 0x00, 0xFF, random) + rename noise + ADS wipe.
    /// </summary>
    public sealed class Dod5220Profile : ShredProfileBase
    {
        public Dod5220Profile() : base(
            id: "dod-5220-3pass",
            displayName: "DoD 5220.22-M (3 passes)",
            passes: new[]
            {
                new OverwritePass("Pass 1 - 0x00", OverwritePatternType.Constant, 0x00),
                new OverwritePass("Pass 2 - 0xFF", OverwritePatternType.Constant, 0xFF),
                new OverwritePass("Pass 3 - Random", OverwritePatternType.Random)
            },
            wipeAds: true, renameNoise: true, renameIterations: 8, wipeMftSlack: true)
        { }
    }

    /// <summary>
    /// Bruce Schneier's 7-pass method (0x00, 0xFF, five random).
    /// </summary>
    public sealed class SchneierSevenPassProfile : ShredProfileBase
    {
        public SchneierSevenPassProfile() : base(
            id: "schneier-7",
            displayName: "Schneier (7 passes)",
            passes: new[]
            {
                new OverwritePass("0x00", OverwritePatternType.Constant, 0x00),
                new OverwritePass("0xFF", OverwritePatternType.Constant, 0xFF),
                new OverwritePass("Random-1", OverwritePatternType.Random),
                new OverwritePass("Random-2", OverwritePatternType.Random),
                new OverwritePass("Random-3", OverwritePatternType.Random),
                new OverwritePass("Random-4", OverwritePatternType.Random),
                new OverwritePass("Random-5", OverwritePatternType.Random)
            },
            wipeAds: true, renameNoise: true, renameIterations: 12, wipeMftSlack: true)
        { }
    }

    public static class BuiltInProfiles
    {
        private static readonly IShredProfile[] _all = new IShredProfile[]
        {
            new ZeroFillProfile(),
            new NistClearProfile(),
            new Dod5220Profile(),
            new SchneierSevenPassProfile()
        };

        public static IReadOnlyList<IShredProfile> GetAll() => _all;

        public static IShredProfile ById(string id)
        {
            var profile = _all.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (profile == null) throw new KeyNotFoundException($"Unknown shred profile id '{id}'.");
            return profile;
        }
    }
}

