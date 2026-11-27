using TrayTranslator.App.Interop;

namespace TrayTranslator.App.Platform;

/// <summary>Keystrokes this app sends for the user (Ctrl+C, Ctrl+V), and checks on the keys held right now.</summary>
internal static class KeyboardInput
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);

    public static bool AnyModifierDown() =>
        IsDown(User32.VK_CONTROL) || IsDown(User32.VK_SHIFT) || IsDown(User32.VK_MENU) || IsDown(User32.VK_LWIN) || IsDown(User32.VK_RWIN);

    /// <summary>
    /// A modifier, letter or digit is down. A letter still held when Ctrl is sent would auto-repeat into a
    /// different shortcut (Ctrl+S), so copying waits for all of them.
    /// </summary>
    public static bool AnyShortcutKeyDown()
    {
        if (AnyModifierDown())
        {
            return true;
        }

        for (var vk = User32.VK_A; vk <= User32.VK_Z; vk++)
        {
            if (IsDown(vk))
            {
                return true;
            }
        }

        for (var vk = User32.VK_0; vk <= User32.VK_9; vk++)
        {
            if (IsDown(vk))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Presses Ctrl+<paramref name="vk"/>. False when Windows did not accept the input.</summary>
    public static unsafe bool SendCtrlChord(ushort vk)
    {
        var inputs = stackalloc User32.INPUT[4];
        inputs[0] = Key((ushort)User32.VK_CONTROL, up: false);
        inputs[1] = Key(vk, up: false);
        inputs[2] = Key(vk, up: true);
        inputs[3] = Key((ushort)User32.VK_CONTROL, up: true);
        return User32.SendInput(4, inputs, sizeof(User32.INPUT)) == 4;
    }

    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken ct)
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

    /// <summary>
    /// Windows silently drops input sent to an elevated window from a non-elevated app (and SendInput does not
    /// report it), so this is checked up front.
    /// </summary>
    public static bool TargetIsElevatedAboveUs(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out var pid);
        var targetElevated = ProcessInterop.IsElevated(pid);
        var weAreElevated = ProcessInterop.IsElevated(ProcessInterop.GetCurrentProcess());
        return targetElevated == true && weAreElevated != true;
    }

    private static bool IsDown(uint vk) => (User32.GetAsyncKeyState((int)vk) & 0x8000) != 0;

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
}
