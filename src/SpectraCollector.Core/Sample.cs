namespace SpectraCollector.Core;

/// <summary>A named sample with its analyzer-local begin and end times.</summary>
public sealed record Sample(string Name, DateTime Begin, DateTime End)
{
    /// <summary>The minute-aligned window used to select spectra.</summary>
    public TimeWindow Window => TimeWindow.Rounded(Begin, End);
}
