using TrayTranslator.App.Interop;

namespace TrayTranslator.App.Platform;

/// <summary>
/// Gives the popup keyboard focus. Windows refuses SetForegroundWindow from a background process, so we
/// briefly attach to the foreground window's input queue, which Windows allows. No keys are sent.
/// </summary>
internal static class ForegroundHelper
{
    public static void Activate(nint hwnd)
    {
        var foreground = User32.GetForegroundWindow();
        if (foreground == hwnd)
        {
            return;
        }

        var foregroundThread = User32.GetWindowThreadProcessId(foreground, out _);
        var ourThread = Kernel32.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != ourThread
            && User32.AttachThreadInput(ourThread, foregroundThread, true);
        try
        {
            User32.BringWindowToTop(hwnd);
            User32.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                User32.AttachThreadInput(ourThread, foregroundThread, false);
            }
        }
    }
}
