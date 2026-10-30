using System.Threading;

namespace Mailcario;

/// <summary>Owns a per-Windows-session mutex and signals the running instance on a second launch.</summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly RegisteredWaitHandle? _registration;
    private readonly bool _ownsMutex;

    public bool IsPrimary => _ownsMutex;

    public SingleInstanceCoordinator(string name, Action onActivate)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("Invalid instance name.", nameof(name));
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\Mailcario.Activate." + name);
        _mutex = new Mutex(true, @"Local\Mailcario.Instance." + name, out bool createdNew);
        if (createdNew) _ownsMutex = true;
        else
        {
            try { _ownsMutex = _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsMutex = true; }
        }
        if (_ownsMutex)
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(_activationEvent,
                (_, timedOut) => { if (!timedOut) onActivate(); }, null, Timeout.Infinite, false);
        }
        else _activationEvent.Set();
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activationEvent.Dispose();
        if (_ownsMutex) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
