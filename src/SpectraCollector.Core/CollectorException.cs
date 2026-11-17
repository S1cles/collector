namespace SpectraCollector.Core;

/// <summary>Base type for expected, user-facing collector failures.</summary>
public abstract class CollectorException : Exception
{
    protected CollectorException(string message) : base(message)
    {
    }

    protected CollectorException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>The sample times input is invalid.</summary>
public sealed class SampleTimesException(string message) : CollectorException(message);

/// <summary>No spectra were found for any sample.</summary>
public sealed class NothingFoundException(string message) : CollectorException(message);

/// <summary>An hour archive exists but cannot be read, usually because the analyzer is packing it.</summary>
public sealed class ArchiveUnreadableException(string path, Exception inner)
    : CollectorException(
        $"Cannot read archive {path}: {inner.Message} If the spectra archiver is packing this hour right now, try again in a few minutes.",
        inner);

/// <summary>A spectrum could not be read while it was being copied into the output zip.</summary>
/// <remarks>A file that has gone missing since the search was most likely packed by the archiver, so the message suggests a rerun.</remarks>
public sealed class SpectrumCopyException(string fileName, string sourcePath, Exception inner)
    : CollectorException($"Cannot read {fileName} from {sourcePath}: {TrimPeriod(inner.Message)}.{RetryHint(inner)}", inner)
{
    private static string TrimPeriod(string message) => message.EndsWith('.') ? message[..^1] : message;

    private static string RetryHint(Exception inner) =>
        inner is FileNotFoundException or DirectoryNotFoundException
            ? " If the spectra archiver packed this hour during collection, run it again."
            : string.Empty;
}

/// <summary>The chosen output folder is inside the analyzer data folder, which must never be written to.</summary>
public sealed class OutputInsideDataException()
    : CollectorException("Choose an output folder outside the LyncisAC data folder.");
