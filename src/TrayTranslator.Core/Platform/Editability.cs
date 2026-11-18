namespace TrayTranslator.Core.Platform;

/// <summary>Whether the place the text was selected in can take a paste.</summary>
public enum SelectionEditability
{
    /// <summary>A text box, editor or chat input: the translation can replace the selection.</summary>
    Editable,

    /// <summary>Text that cannot be edited, such as a web page or a label: show the translation instead.</summary>
    ReadOnly,

    /// <summary>The app does not say. Try the paste, as before.</summary>
    Unknown,
}

/// <summary>The broad kind of control that has keyboard focus, as accessibility reports it.</summary>
public enum FocusedControlKind
{
    /// <summary>Nothing could be read.</summary>
    Unknown,

    /// <summary>A text box or text editor.</summary>
    Edit,

    /// <summary>A whole document (a web page, a Word document); can be either.</summary>
    Document,

    /// <summary>A pane, window, group or custom control: often a custom editor, so it can be either.</summary>
    Container,

    /// <summary>Static text, links, buttons, list items, images: never editable.</summary>
    StaticContent,
}

/// <param name="Kind">What kind of control has focus.</param>
/// <param name="SelectionReadOnly">Whether the selected text itself is read-only (UI Automation text attribute),
/// when the control says. The most direct answer for web pages, Word and editors.</param>
/// <param name="AccessibilityReadOnly">The control's own "read-only" flag (UI Automation Value pattern), when it has one.</param>
/// <param name="ClassicEditReadOnly">For a classic Windows text box (Edit / RichEdit), its read-only style.</param>
public readonly record struct FocusedControlFacts(
    FocusedControlKind Kind,
    bool? SelectionReadOnly = null,
    bool? AccessibilityReadOnly = null,
    bool? ClassicEditReadOnly = null);

/// <summary>Turns what accessibility reports about the focused control into a paste decision.</summary>
public static class EditabilityRules
{
    public static SelectionEditability Classify(FocusedControlFacts facts)
    {
        // A classic text box knows exactly whether it is read-only.
        if (facts.ClassicEditReadOnly is { } classic)
        {
            return classic ? SelectionEditability.ReadOnly : SelectionEditability.Editable;
        }

        // The selected text itself: read-only on a web page, editable in a text box or document.
        if (facts.SelectionReadOnly is { } selection)
        {
            return selection ? SelectionEditability.ReadOnly : SelectionEditability.Editable;
        }

        // A control that reports a read-only flag (browsers report true for the page itself).
        if (facts.AccessibilityReadOnly is { } readOnly)
        {
            return readOnly ? SelectionEditability.ReadOnly : SelectionEditability.Editable;
        }

        return facts.Kind switch
        {
            FocusedControlKind.Edit => SelectionEditability.Editable,
            FocusedControlKind.StaticContent => SelectionEditability.ReadOnly,
            _ => SelectionEditability.Unknown,
        };
    }

    /// <summary>Automatic replace pastes unless the selection is known to be read-only.</summary>
    public static bool CanTryReplace(SelectionEditability editability) => editability != SelectionEditability.ReadOnly;
}
