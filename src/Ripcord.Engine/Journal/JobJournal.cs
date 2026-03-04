
// =============================
// File: src/Ripcord.Engine/Journal/JobJournal.cs
// =============================
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Ripcord.Engine.Journal
{
    /// <summary>
    /// High-throughput, rolling JSONL journal writer with retention by size and count.
    /// </summary>
    public sealed class JobJournal : IAsyncDisposable
    {
        private readonly DirectoryInfo _dir;
        private readonly long _maxBytesPerFile;
        private readonly int _maxFiles;
        private readonly Channel<JournalRecord> _channel;
        private readonly CancellationTokenSource _cts = new();
        private Task? _pump;

        public JobJournal(string? directory = null, long maxBytesPerFile = 16 * 1024 * 1024, int maxFiles = 8)
        {
            _dir = new DirectoryInfo(directory ?? GetDefaultDirectory());
            if (!_dir.Exists) _dir.Create();
            _maxBytesPerFile = Math.Max(256 * 1024, maxBytesPerFile);
            _maxFiles = Math.Max(1, maxFiles);
            _channel = Channel.CreateBounded<JournalRecord>(new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
            _pump = Task.Run(PumpAsync);
        }

        public ValueTask WriteAsync(JournalRecord rec) => _channel.Writer.WriteAsync(rec);

        public async ValueTask DisposeAsync()
        {
            try { _channel.Writer.TryComplete(); } catch { }
            try { _cts.Cancel(); } catch { }
            if (_pump != null) { try { await _pump.ConfigureAwait(false); } catch { } }
            _cts.Dispose();
        }

        private async Task PumpAsync()
        {
            FileStream? stream = null;
            StreamWriter? writer = null;
            string? currentPath = null;
            long currentSize = 0;

            try
            {
                while (await _channel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                {
                    while (_channel.Reader.TryRead(out var rec))
                    {
                        if (stream == null || currentSize > _maxBytesPerFile)
                        {
                            writer?.Dispose();
                            stream?.Dispose();
                            (stream, writer, currentPath) = OpenNextFile();
                            currentSize = stream!.Length;
                        }

                        string json = JsonSerializer.Serialize(rec);
                        writer!.WriteLine(json);
                        await writer.FlushAsync().ConfigureAwait(false);
                        currentSize += Encoding.UTF8.GetByteCount(json) + 2;
                    }
                }
            }
            catch (OperationCanceledException) { /* shutting down */ }
            finally
            {
                writer?.Dispose();
                stream?.Dispose();
            }
        }

        private (FileStream stream, StreamWriter writer, string path) OpenNextFile()
        {
            RollIfNeeded();
            string name = $"engine-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.jsonl";
            string path = Path.Combine(_dir.FullName, name);
            var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return (fs, sw, path);
        }

        private void RollIfNeeded()
        {
            var files = _dir.GetFiles("engine-*.jsonl");
            if (files.Length < _maxFiles) return;
            Array.Sort(files, (a, b) => a.CreationTimeUtc.CompareTo(b.CreationTimeUtc));
            for (int i = 0; i <= files.Length - _maxFiles; i++)
            {
                try { files[i].Delete(); } catch { /* best-effort */ }
            }
        }

        private static string GetDefaultDirectory()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(root, "Ripcord", "Logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
