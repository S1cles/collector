using System.IO.Compression;

namespace SpectraCollector.Core;

/// <summary>A spectrum file on the analyzer, either loose or inside an hour archive.</summary>
/// <param name="Name">The parsed file name.</param>
/// <param name="SourcePath">The loose file, or the archive that holds the entry.</param>
/// <param name="EntryName">The entry's full name in the archive; null for a loose file.</param>
public sealed record FoundSpectrum(SpectrumName Name, string SourcePath, string? EntryName)
{
    private const FileShare Sharing = FileShare.ReadWrite | FileShare.Delete;

    /// <summary>Opens the content read-only; disposing the stream releases the archive too.</summary>
    public Stream Open()
    {
        var file = new FileStream(SourcePath, FileMode.Open, FileAccess.Read, Sharing);
        if (EntryName is null)
        {
            return file;
        }

        try
        {
            var archive = new ZipArchive(file, ZipArchiveMode.Read);
            try
            {
                var entry = archive.GetEntry(EntryName)
                    ?? throw new FileNotFoundException($"Entry {EntryName} is missing from {SourcePath}.");
                return new ArchiveEntryStream(entry.Open(), archive, file);
            }
            catch
            {
                archive.Dispose();
                throw;
            }
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    internal static FileStream OpenShared(string path) => new(path, FileMode.Open, FileAccess.Read, Sharing);

    private sealed class ArchiveEntryStream(Stream entry, ZipArchive archive, FileStream file) : Stream
    {
        public override bool CanRead => entry.CanRead;
        public override bool CanSeek => entry.CanSeek;
        public override bool CanWrite => false;
        public override long Length => entry.Length;
        public override long Position
        {
            get => entry.Position;
            set => entry.Position = value;
        }

        public override void Flush() => entry.Flush();
        public override int Read(byte[] buffer, int offset, int count) => entry.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => entry.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => entry.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                entry.Dispose();
                archive.Dispose();
                file.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
