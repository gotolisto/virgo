using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using TrayTranslator.App.Interop;
using TrayTranslator.Core.Platform;

namespace TrayTranslator.App.Platform;

/// <summary>
/// Reads what Windows knows about the focused control in another app: the read-only style of a classic text box,
/// and through UI Automation (the accessibility API screen readers use) whether the selected text and the
/// control are read-only, and what kind of control it is. Reads only; never changes anything.
/// </summary>
internal static class WindowsEditabilityProbe
{
    public static FocusedControlFacts Read(nint targetWindow)
    {
        var threadId = User32.GetWindowThreadProcessId(targetWindow, out var processId);
        var classic = ClassicEditReadOnly(threadId);
        var (kind, selectionReadOnly, controlReadOnly) = ReadAutomation(processId);
        return new FocusedControlFacts(kind, selectionReadOnly, controlReadOnly, classic);
    }

    /// <summary>For a classic Windows text box (Edit / RichEdit, e.g. Notepad), its ES_READONLY style; else null.</summary>
    private static unsafe bool? ClassicEditReadOnly(uint threadId)
    {
        var info = new User32.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<User32.GUITHREADINFO>() };
        if (threadId == 0 || !User32.GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == 0)
        {
            return null;
        }

        var buffer = stackalloc char[256];
        var length = User32.GetClassName(info.hwndFocus, buffer, 256);
        var className = length > 0 ? new string(buffer, 0, length) : string.Empty;
        var isClassicEdit = className.StartsWith("Edit", StringComparison.OrdinalIgnoreCase)
            || className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase);
        if (!isClassicEdit)
        {
            return null;
        }

        var style = (long)User32.GetWindowLongPtr(info.hwndFocus, User32.GWL_STYLE);
        return (style & User32.ES_READONLY) != 0;
    }

    private static (FocusedControlKind Kind, bool? SelectionReadOnly, bool? ControlReadOnly) ReadAutomation(uint processId)
    {
        try
        {
            var element = AutomationElement.FocusedElement;

            // Focus already moved to another app (or to us): what we would read is not the selection's.
            if (element is null || (uint)element.Current.ProcessId != processId)
            {
                return (FocusedControlKind.Unknown, null, null);
            }

            var kind = Map(element.Current.ControlType);
            bool? controlReadOnly = element.TryGetCurrentPattern(ValuePattern.Pattern, out var value)
                ? ((ValuePattern)value).Current.IsReadOnly
                : null;

            return (kind, SelectionReadOnly(element), controlReadOnly);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException)
        {
            return (FocusedControlKind.Unknown, null, null);
        }
    }

    /// <summary>The "read-only" text attribute of the current selection, when the control reports one.</summary>
    private static bool? SelectionReadOnly(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
        {
            return null;
        }

        var text = (TextPattern)pattern;
        if (text.SupportedTextSelection == SupportedTextSelection.None)
        {
            return null;
        }

        var ranges = text.GetSelection();
        if (ranges.Length == 0)
        {
            return null;
        }

        // A mixed selection, or one the app does not describe, says nothing either way.
        return ranges[0].GetAttributeValue(TextPattern.IsReadOnlyAttribute) is bool readOnly ? readOnly : null;
    }

    private static FocusedControlKind Map(ControlType type)
    {
        if (type == ControlType.Edit)
        {
            return FocusedControlKind.Edit;
        }

        if (type == ControlType.Document)
        {
            return FocusedControlKind.Document;
        }

        if (type == ControlType.Pane || type == ControlType.Window || type == ControlType.Group
            || type == ControlType.Custom || type == ControlType.ComboBox)
        {
            return FocusedControlKind.Container;
        }

        if (type == ControlType.Text || type == ControlType.Hyperlink || type == ControlType.Button
            || type == ControlType.Image || type == ControlType.ListItem || type == ControlType.MenuItem
            || type == ControlType.TreeItem || type == ControlType.DataItem || type == ControlType.TabItem
            || type == ControlType.HeaderItem || type == ControlType.CheckBox || type == ControlType.RadioButton
            || type == ControlType.StatusBar || type == ControlType.ToolTip || type == ControlType.TitleBar)
        {
            return FocusedControlKind.StaticContent;
        }

        return FocusedControlKind.Unknown;
    }
}
