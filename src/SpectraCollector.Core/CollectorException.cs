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
