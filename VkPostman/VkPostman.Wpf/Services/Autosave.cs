using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VkPostman.Wpf.Services;

public enum AutosaveStatus
{
    Idle,
    Dirty,
    Saving,
    Saved,
    Error,
}

/// <summary>
/// Poll-based autosave for POCO editables. On each tick it serializes the
/// current getter result and compares it to the last-saved signature; if
/// they differ, it calls the save function.
///
/// Poll-based rather than event-based because our editable types
/// (<c>PostTemplate</c>, <c>TargetGroup</c>, …) are plain POCOs — WPF's
/// TextBox bindings update them on each keystroke, but the POCOs don't
/// implement INPC so we can't subscribe to changes.
/// </summary>
public sealed partial class Autosave<T> : ObservableObject where T : class
{
    private readonly DispatcherTimer _timer;
    private readonly Func<T?> _get;
    private readonly Func<T, Task> _save;
    private string _lastSignature = "";
    private bool _inflight = false;

    [ObservableProperty] private AutosaveStatus status = AutosaveStatus.Idle;
    [ObservableProperty] private string? lastError;

    public Autosave(Func<T?> get, Func<T, Task> save, TimeSpan? pollInterval = null)
    {
        _get  = get;
        _save = save;
        _timer = new DispatcherTimer { Interval = pollInterval ?? TimeSpan.FromMilliseconds(500) };
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public void Start() => _timer.Start();
    public void Stop()  => _timer.Stop();

    /// <summary>
    /// Called when the user swaps to a different editable (e.g. picks another
    /// group in the list). Flushes any pending save, then resets the baseline
    /// signature so the new item's "no change" state is correctly seen as clean.
    /// </summary>
    public async Task ResetAsync()
    {
        await FlushAsync();
        _lastSignature = Serialize(_get());
        Status = AutosaveStatus.Idle;
    }

    public async Task FlushAsync()
    {
        _timer.Stop();
        try { await TickAsync(force: true); }
        finally { _timer.Start(); }
    }

    private async Task TickAsync(bool force = false)
    {
        if (_inflight) return;
        var current = _get();
        if (current is null) { Status = AutosaveStatus.Idle; return; }

        var sig = Serialize(current);
        if (!force && sig == _lastSignature)
        {
            if (Status == AutosaveStatus.Dirty) Status = AutosaveStatus.Saved;
            return;
        }

        // Show "Dirty" briefly before the next tick actually persists.
        if (Status == AutosaveStatus.Idle || Status == AutosaveStatus.Saved)
            Status = AutosaveStatus.Dirty;

        _inflight = true;
        Status    = AutosaveStatus.Saving;
        try
        {
            await _save(current);
            _lastSignature = sig;
            LastError      = null;
            Status         = AutosaveStatus.Saved;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Status    = AutosaveStatus.Error;
        }
        finally
        {
            _inflight = false;
        }
    }

    private static string Serialize(object? obj)
        => obj is null ? "" : JsonSerializer.Serialize(obj);
}
