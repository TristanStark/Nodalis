using System.Diagnostics;
using System.Text;
using System.Windows.Threading;

namespace Nodalis.App.Diagnostics;

/// <summary>
/// Records bounded dispatcher scheduling delays in memory, without collecting
/// document content, filenames or network telemetry.
/// </summary>
public sealed class DispatcherHeartbeatMonitor
{
    private const int IntervalMilliseconds = 500;
    private const int MaximumSamples = 2048;
    private const int MaximumSlowEvents = 32;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();
    private readonly long[] _samples = new long[MaximumSamples];
    private readonly List<(DateTimeOffset WhenUtc, long DelayMilliseconds)> _slowEvents = [];
    private int _count;
    private int _next;
    private long _previousTickMilliseconds;
    private long _totalTicks;
    private long _slowTickCount;
    private long _maximumDelay;

    /// <summary>Creates a disabled monitor that uses the supplied UI dispatcher.</summary>
    public DispatcherHeartbeatMonitor(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(IntervalMilliseconds),
            DispatcherPriority.Background,
            OnTimerTick,
            dispatcher)
        {
            IsEnabled = false
        };
    }

    /// <summary>Gets whether sampling is enabled for the current session.</summary>
    public bool IsEnabled => _timer.IsEnabled;

    /// <summary>Enables or disables sampling without discarding the collected samples.</summary>
    public void SetEnabled(bool enabled)
    {
        if (_timer.IsEnabled == enabled)
        {
            return;
        }

        if (!enabled)
        {
            _timer.Stop();
            _stopwatch.Stop();
            return;
        }

        _stopwatch.Restart();
        _previousTickMilliseconds = 0;
        _timer.Start();
    }

    /// <summary>Handles the low-priority dispatcher heartbeat.</summary>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        long elapsed = _stopwatch.ElapsedMilliseconds;
        long delay = Math.Max(
            0,
            elapsed - _previousTickMilliseconds - IntervalMilliseconds);
        _previousTickMilliseconds = elapsed;

        _samples[_next] = delay;
        _next = (_next + 1) % MaximumSamples;
        _count = Math.Min(_count + 1, MaximumSamples);
        _totalTicks++;
        _maximumDelay = Math.Max(_maximumDelay, delay);

        if (delay <= 200)
        {
            return;
        }

        _slowTickCount++;
        if (_slowEvents.Count >= MaximumSlowEvents)
        {
            _slowEvents.RemoveAt(0);
        }

        _slowEvents.Add((DateTimeOffset.UtcNow, delay));
    }

    /// <summary>
    /// Builds a local performance report using bounded in-memory delays.
    /// The snapshot contains only durations and UTC timestamps.
    /// </summary>
    public string BuildReport()
    {
        long[] sorted = new long[_count];
        Array.Copy(_samples, sorted, _count);
        Array.Sort(sorted);

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Performances WPF — heartbeat Dispatcher");
        builder.AppendLine($"Mesure : {(IsEnabled ? "active" : "arrêtée")}");
        builder.AppendLine($"Intervalle de référence : {IntervalMilliseconds} ms");
        builder.AppendLine($"Échantillons retenus : {sorted.Length} / {MaximumSamples}");
        builder.AppendLine($"Échantillons totaux : {_totalTicks}");
        builder.AppendLine($"Retards > 200 ms : {_slowTickCount}");
        builder.AppendLine($"Retard p50 : {Percentile(sorted, 0.50)} ms");
        builder.AppendLine($"Retard p95 : {Percentile(sorted, 0.95)} ms");
        builder.AppendLine($"Retard p99 : {Percentile(sorted, 0.99)} ms");
        builder.AppendLine($"Retard maximum : {_maximumDelay} ms");
        builder.AppendLine("Derniers retards > 200 ms (UTC, maximum 32) :");

        if (_slowEvents.Count == 0)
        {
            builder.AppendLine("(aucun)");
        }
        else
        {
            foreach ((DateTimeOffset when, long delay) in _slowEvents)
            {
                builder.AppendLine($"{when:O} — {delay} ms");
            }
        }

        builder.AppendLine(
            "Mesures conservées en mémoire uniquement ; aucun document ou chemin n'est enregistré.");
        return builder.ToString();
    }

    /// <summary>Returns a nearest-rank percentile for the supplied sorted delays.</summary>
    private static long Percentile(IReadOnlyList<long> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        int index = Math.Clamp(
            (int)Math.Ceiling(sorted.Count * percentile) - 1,
            0,
            sorted.Count - 1);
        return sorted[index];
    }
}
