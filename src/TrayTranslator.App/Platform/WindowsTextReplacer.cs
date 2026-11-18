using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TrayTranslator.App.Interop;
using TrayTranslator.Core.Platform;

namespace TrayTranslator.App.Platform;

/// <summary>
/// Replaces the selection in another window: translation onto the clipboard, focus back to that window, Ctrl+V,
/// then the user's clipboard text put back. Call from the UI (STA) thread.
/// <para>
/// Windows does not let a normal app send input to a window running as administrator; such a paste fails and
/// the caller falls back to the popup.
/// </para>
/// </summary>
internal sealed partial class WindowsTextReplacer : ITextReplacer
{
    /// <summary>How long to wait for the user to let go of Ctrl (and other modifiers) before pasting.</summary>
    private static readonly TimeSpan ModifierWait = TimeSpan.FromSeconds(2);

    /// <summary>How long the target window gets to come to the foreground.</summary>
    private static readonly TimeSpan FocusWait = TimeSpan.FromMilliseconds(600);

    /// <summary>How long the target app gets to read the clipboard before the old text is put back.</summary>
    private static readonly TimeSpan PasteSettle = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);

    private readonly ILogger _logger;

    public WindowsTextReplacer(ILogger<WindowsTextReplacer> logger)
    {
        _logger = logger;
    }

    public FocusTarget CaptureFocus()
    {
        var hwnd = User32.GetForegroundWindow();
        if (hwnd == 0)
        {
            return FocusTarget.None;
        }

        User32.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId ? FocusTarget.None : new FocusTarget(hwnd);
    }

    /// <summary>UI Automation can be slow the first time a browser is asked; never wait longer than this.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(800);

    public async Task<SelectionEditability> ProbeEditabilityAsync(FocusTarget target, CancellationToken ct)
    {
        if (target.IsNone)
        {
            // No other window to paste into (e.g. the text was selected in our own popup).
            return SelectionEditability.ReadOnly;
        }

        try
        {
            // Accessibility calls go to the other app; keep them off the UI thread.
            var facts = await Task.Run(() => WindowsEditabilityProbe.Read(target.Handle), ct).WaitAsync(ProbeTimeout, ct);
            var result = EditabilityRules.Classify(facts);
            LogProbe(_logger, facts.Kind, facts.SelectionReadOnly, facts.AccessibilityReadOnly, facts.ClassicEditReadOnly, result);
            return result;
        }
        catch (TimeoutException)
        {
            LogProbeTimeout(_logger);
            return SelectionEditability.Unknown;
        }
        catch (OperationCanceledException)
        {
            return SelectionEditability.Unknown;
        }
#pragma warning disable CA1031 // The probe is advisory: any failure means "try the paste, as before".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogProbeFailed(_logger, ex.GetType().Name);
            return SelectionEditability.Unknown;
        }
    }

    public async Task<bool> ReplaceSelectionAsync(FocusTarget target, string replacement, string? clipboardToRestore, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (target.IsNone || !User32.IsWindow(target.Handle))
        {
            LogNoTarget(_logger);
            return false;
        }

        if (TargetIsElevatedAboveUs(target.Handle))
        {
            LogElevated(_logger);
            return false;
        }

        if (!await ClipboardWriter.TrySetTextAsync(replacement))
        {
            LogClipboardBusy(_logger);
            return false;
        }

        try
        {
            ForegroundHelper.Activate(target.Handle);
            if (!await WaitUntilAsync(() => User32.GetForegroundWindow() == target.Handle, FocusWait, ct))
            {
                LogNoFocus(_logger);
                return false;
            }

            // Ctrl may still be held from Ctrl+C+C; a paste mixed with held keys would misfire.
            await WaitUntilAsync(() => !AnyModifierDown(), ModifierWait, ct);

            if (!SendCtrlV())
            {
                LogInputBlocked(_logger, Marshal.GetLastPInvokeError());
                return false;
            }

            // The target app reads the clipboard when it processes the paste; give it time before restoring.
            await Task.Delay(PasteSettle, ct);
            LogReplaced(_logger);
            return true;
        }
        finally
        {
            if (clipboardToRestore is not null)
            {
                await ClipboardWriter.TrySetTextAsync(clipboardToRestore);
            }
        }
    }

    /// <summary>
    /// Windows silently drops input sent to an elevated window from a non-elevated app (and SendInput does not
    /// report it), so this is checked up front.
    /// </summary>
    private static bool TargetIsElevatedAboveUs(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out var pid);
        var targetElevated = ProcessInterop.IsElevated(pid);
        var weAreElevated = ProcessInterop.IsElevated(ProcessInterop.GetCurrentProcess());
        return targetElevated == true && weAreElevated != true;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(Poll, ct);
        }

        return true;
    }

    private static bool AnyModifierDown() =>
        IsDown(User32.VK_CONTROL) || IsDown(User32.VK_SHIFT) || IsDown(User32.VK_MENU) || IsDown(User32.VK_LWIN) || IsDown(User32.VK_RWIN);

    private static bool IsDown(uint vk) => (User32.GetAsyncKeyState((int)vk) & 0x8000) != 0;

    private static unsafe bool SendCtrlV()
    {
        var inputs = stackalloc User32.INPUT[4];
        inputs[0] = Key((ushort)User32.VK_CONTROL, up: false);
        inputs[1] = Key(User32.VK_V, up: false);
        inputs[2] = Key(User32.VK_V, up: true);
        inputs[3] = Key((ushort)User32.VK_CONTROL, up: true);
        return User32.SendInput(4, inputs, sizeof(User32.INPUT)) == 4;
    }

    private static User32.INPUT Key(ushort vk, bool up) => new()
    {
        type = User32.INPUT_KEYBOARD,
        U = new User32.InputUnion
        {
            ki = new User32.KEYBDINPUT
            {
                wVk = vk,
                dwFlags = up ? User32.KEYEVENTF_KEYUP : 0,
                dwExtraInfo = User32.InjectedMarker,
            },
        },
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Selection editability: {Result} (kind {Kind}, selection read-only {Selection}, control read-only {Control}, classic edit read-only {Classic})")]
    private static partial void LogProbe(ILogger logger, FocusedControlKind kind, bool? selection, bool? control, bool? classic, SelectionEditability result);

    [LoggerMessage(Level = LogLevel.Information, Message = "Selection editability: no answer in time; the paste will be tried")]
    private static partial void LogProbeTimeout(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Selection editability could not be read ({ErrorType}); the paste will be tried")]
    private static partial void LogProbeFailed(ILogger logger, string errorType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Replaced the selection in the original window")]
    private static partial void LogReplaced(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Replace skipped: the original window is gone or belongs to this app")]
    private static partial void LogNoTarget(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Replace skipped: the original window runs as administrator")]
    private static partial void LogElevated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Replace failed: the clipboard stayed locked")]
    private static partial void LogClipboardBusy(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Replace failed: the original window did not take focus")]
    private static partial void LogNoFocus(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Replace failed: Windows did not accept the paste keystroke (error {Error})")]
    private static partial void LogInputBlocked(ILogger logger, int error);
}
