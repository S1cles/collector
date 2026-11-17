namespace SpectraCollector.Core;

/// <summary>Base type for expected, user-facing collector failures.</summary>
public abstract class CollectorException(string message) : Exception(message);

/// <summary>The sample times input is invalid.</summary>
public sealed class SampleTimesException(string message) : CollectorException(message);

/// <summary>No spectra were found for any sample.</summary>
public sealed class NothingFoundException(string message) : CollectorException(message);
