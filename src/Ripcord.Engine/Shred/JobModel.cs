using System;
using System.IO;
using Ripcord.Engine.Shred;

namespace Ripcord.Engine.Shred
{
    /// <summary>Request to shred a file or directory.</summary>
    public sealed record ShredJobRequest(
        string TargetPath,
        IShredProfile Profile,
        bool DryRun = true,
        bool DeleteAfter = true,
        bool Recurse = true);

    /// <summary>Point-in-time progress snapshot.</summary>
    public sealed record ShredJobProgress(
        Guid JobId,
        string? CurrentFile,
        int? PassIndex,
        long? BytesProcessed,
        long? BytesTotal,
        double? Percent,
        string? Status);

    /// <summary>Final job result summary.</summary>
    public sealed record ShredJobResult(
        Guid JobId,
        int FilesProcessed,
        int FilesFailed,
        long BytesOverwritten,
        long FilesDeleted);
}
