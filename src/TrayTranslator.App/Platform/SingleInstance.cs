namespace TrayTranslator.App.Platform;

/// <summary>
/// One running copy per user session (named mutex). A second launch signals the first to show Settings,
/// then exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\TrayTranslator.SingleInstance";
    private const string ShowEventName = @"Local\TrayTranslator.ShowSettings";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, bool isFirst)
    {
        _mutex = mutex;
        IsFirst = isFirst;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
    }

    public bool IsFirst { get; }

    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                // The previous owner may have crashed without releasing it.
                createdNew = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
            }
        }

        return new SingleInstance(mutex, createdNew);
    }

    /// <summary>Second instance: ask the first one to show its Settings window.</summary>
    public void SignalFirstInstance() => _showEvent?.Set();

    /// <summary>First instance: run <paramref name="onShowRequested"/> (on a pool thread) when signalled.</summary>
    public void ListenForSecondInstances(Action onShowRequested)
    {
        if (_showEvent is null)
        {
            return;
        }

        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showEvent?.Dispose();
        if (IsFirst)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
    }
}
