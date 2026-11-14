using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TrayTranslator.App.Interop;
using TrayTranslator.Core.Input;
using TrayTranslator.Core.Platform;

namespace TrayTranslator.App.Platform;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL) that detects Ctrl+C+C.
/// <para>
/// The hook runs on its own thread with its own message loop, so a busy UI thread can never make Windows
/// time the hook out. The callback only copies three numbers into a channel and passes every key on
/// unchanged (<c>CallNextHookEx</c>); the detector runs on a separate worker thread.
/// </para>
/// </summary>
internal sealed partial class WindowsKeyboardHook : IHotkeyListener
{
    // A low-level hook callback must be a static method; only one hook exists per process.
    private static WindowsKeyboardHook? s_active;

    private readonly ILogger<WindowsKeyboardHook> _logger;
    private readonly DoublePressDetector _detector;
    private readonly Channel<KeyEvent> _events = Channel.CreateBounded<KeyEvent>(
        new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });

    private Thread? _hookThread;
    private uint _hookThreadId;
    private Task? _worker;
    private nint _hook;
    private volatile bool _paused;
    private volatile bool _resetRequested;

    public WindowsKeyboardHook(TimeSpan interval, ILogger<WindowsKeyboardHook> logger)
    {
        _logger = logger;
        _detector = new DoublePressDetector(interval);
    }

    public event EventHandler? Triggered;

    public TimeSpan Interval
    {
        get => _detector.Interval;
        set => _detector.Interval = value;
    }

    public bool IsPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            _resetRequested = true;
        }
    }

    public void Start()
    {
        if (_hookThread is not null)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref s_active, this, null) is not null)
        {
            throw new InvalidOperationException("Only one keyboard hook may run per process.");
        }

        _worker = Task.Factory.StartNew(ProcessEventsAsync, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

        using var installed = new ManualResetEventSlim();
        _hookThread = new Thread(() => HookThreadMain(installed))
        {
            IsBackground = true,
            Name = "Keyboard hook",
            Priority = ThreadPriority.AboveNormal,
        };
        _hookThread.Start();
        installed.Wait(TimeSpan.FromSeconds(5));
    }

    public void Stop()
    {
        var thread = _hookThread;
        if (thread is null)
        {
            return;
        }

        User32.PostThreadMessage(_hookThreadId, User32.WM_QUIT, 0, 0);
        thread.Join(TimeSpan.FromSeconds(2));
        _hookThread = null;
        _events.Writer.TryComplete();
        _worker?.Wait(TimeSpan.FromSeconds(2));
        Interlocked.CompareExchange(ref s_active, null, this);
    }

    public void Dispose() => Stop();

    private unsafe void HookThreadMain(ManualResetEventSlim installed)
    {
        _hookThreadId = Kernel32.GetCurrentThreadId();

        // Make sure this thread has a message queue before anyone posts WM_QUIT to it.
        User32.PeekMessage(out _, 0, User32.WM_APP, User32.WM_APP, 0);

        _hook = User32.SetWindowsHookEx(User32.WH_KEYBOARD_LL, &HookCallback, Kernel32.GetModuleHandle(null), 0);
        if (_hook == 0)
        {
            LogInstallFailed(_logger, Marshal.GetLastPInvokeError());
        }
        else
        {
            LogInstalled(_logger);
        }

        installed.Set();

        // Low-level hooks are called through this thread's message loop.
        while (User32.GetMessage(out _, 0, 0, 0) > 0)
        {
        }

        if (_hook != 0)
        {
            User32.UnhookWindowsHookEx(_hook);
            _hook = 0;
            LogRemoved(_logger);
        }
    }

    [UnmanagedCallersOnly]
    private static unsafe nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        var self = s_active;
        if (nCode == User32.HC_ACTION && self is not null)
        {
            try
            {
                var data = (User32.KBDLLHOOKSTRUCT*)lParam;
                var message = (int)wParam;
                var isDown = message is User32.WM_KEYDOWN or User32.WM_SYSKEYDOWN;
                var isUp = message is User32.WM_KEYUP or User32.WM_SYSKEYUP;
                if (isDown || isUp)
                {
                    var kind = data->vkCode switch
                    {
                        User32.VK_CONTROL or User32.VK_LCONTROL or User32.VK_RCONTROL => KeyKind.Ctrl,
                        User32.VK_C => KeyKind.C,
                        _ => KeyKind.Other,
                    };

                    // Never blocks: the channel drops the oldest event if the worker ever falls behind.
                    self._events.Writer.TryWrite(new KeyEvent(kind, isDown, Environment.TickCount64));
                }
            }
#pragma warning disable CA1031 // An exception must never escape into Windows from a hook callback.
            catch
            {
            }
#pragma warning restore CA1031
        }

        // Always pass the key on unchanged: the first Ctrl+C stays a normal copy.
        return User32.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private async Task ProcessEventsAsync()
    {
        await foreach (var e in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (_resetRequested)
            {
                _resetRequested = false;
                _detector.Reset();
            }

            if (_paused || !_detector.Process(e))
            {
                continue;
            }

            LogTriggered(_logger);
            try
            {
                Triggered?.Invoke(this, EventArgs.Empty);
            }
#pragma warning disable CA1031 // A failing handler must not stop the hook from working.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogHandlerFailed(_logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Keyboard hook installed")]
    private static partial void LogInstalled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Keyboard hook could not be installed (Win32 error {Error})")]
    private static partial void LogInstallFailed(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keyboard hook removed")]
    private static partial void LogRemoved(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ctrl+C+C detected")]
    private static partial void LogTriggered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Trigger handler failed")]
    private static partial void LogHandlerFailed(ILogger logger, Exception ex);
}
