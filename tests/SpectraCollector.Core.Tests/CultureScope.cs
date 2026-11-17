using System.Globalization;

namespace SpectraCollector.Core.Tests;

/// <summary>Sets the current culture until disposed, e.g. to pick the ANSI code page a CSV is decoded with.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

    public void Dispose() => CultureInfo.CurrentCulture = _previous;
}
