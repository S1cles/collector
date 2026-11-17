using System.Windows.Threading;
using SpectraCollector.Core;

namespace SpectraCollector.App;

/// <summary>
/// Takes collection progress on the worker thread and keeps only the newest event, plus the report of the first
/// Writing event; a dispatcher timer on the UI thread shows that state at most every 50 ms. An 8-hour run reports
/// about 6000 files, and this keeps each of them to a field write instead of a dispatcher round trip.
/// The timer runs from construction until <see cref="Stop"/> or <see cref="Dispose"/>.
/// </summary>
internal sealed class ProgressPump : IProgress<CollectProgress>, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private readonly Lock _gate = new();
    private readonly Action<CollectProgress, CollectionReport?> _show;
    private readonly DispatcherTimer _timer;
    private CollectProgress? _latest;
    private CollectionReport? _report;
    private long _received;
    private long _shown;

    /// <summary>Starts the timer on the calling (UI) thread's dispatcher; <paramref name="show"/> runs on that thread.</summary>
    public ProgressPump(Action<CollectProgress, CollectionReport?> show)
    {
        _show = show;
        _timer = new DispatcherTimer(
            Interval, DispatcherPriority.Background, (_, _) => ShowLatest(), Dispatcher.CurrentDispatcher);
    }

    /// <inheritdoc />
    public void Report(CollectProgress value)
    {
        lock (_gate)
        {
            _latest = value;
            _report ??= value.Report;
            _received++;
        }
    }

    /// <summary>Stops the timer and shows what arrived after the last tick, so the sheet ends on the final state.</summary>
    public void Stop()
    {
        _timer.Stop();
        ShowLatest();
    }

    /// <inheritdoc />
    public void Dispose() => _timer.Stop();

    private void ShowLatest()
    {
        CollectProgress? latest;
        CollectionReport? report;
        long received;
        lock (_gate)
        {
            latest = _latest;
            report = _report;
            received = _received;
        }

        if (latest is null || received == _shown)
        {
            return;
        }

        _shown = received;
        _show(latest, report);
    }
}
