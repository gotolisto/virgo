using TrayTranslator.Core.Platform;

namespace TrayTranslator.Tests.Platform;

public class EditabilityRulesTests
{
    [Theory]
    // The selected text's own read-only attribute decides first (after a classic text box's style).
    [InlineData(FocusedControlKind.Document, true, false, SelectionEditability.ReadOnly)]
    [InlineData(FocusedControlKind.Document, false, true, SelectionEditability.Editable)]
    [InlineData(FocusedControlKind.StaticContent, false, null, SelectionEditability.Editable)]
    public void Selection_attribute_comes_before_the_control_flag(FocusedControlKind kind, bool selectionReadOnly, bool? controlReadOnly, SelectionEditability expected)
    {
        Assert.Equal(expected, EditabilityRules.Classify(new FocusedControlFacts(kind, selectionReadOnly, controlReadOnly)));
    }

    [Fact]
    public void Classic_text_box_style_beats_the_selection_attribute()
    {
        Assert.Equal(SelectionEditability.ReadOnly, EditabilityRules.Classify(new FocusedControlFacts(FocusedControlKind.Edit, false, false, true)));
    }

    [Theory]
    // A read-only flag decides, whatever the kind (a browser page is a Document with read-only = true).
    [InlineData(FocusedControlKind.Document, true, null, SelectionEditability.ReadOnly)]
    [InlineData(FocusedControlKind.Document, false, null, SelectionEditability.Editable)]
    [InlineData(FocusedControlKind.Edit, true, null, SelectionEditability.ReadOnly)]
    [InlineData(FocusedControlKind.Container, false, null, SelectionEditability.Editable)]
    // A classic text box's own style wins over everything.
    [InlineData(FocusedControlKind.Edit, false, true, SelectionEditability.ReadOnly)]
    [InlineData(FocusedControlKind.Unknown, null, false, SelectionEditability.Editable)]
    // Without flags, the kind decides.
    [InlineData(FocusedControlKind.Edit, null, null, SelectionEditability.Editable)]
    [InlineData(FocusedControlKind.StaticContent, null, null, SelectionEditability.ReadOnly)]
    [InlineData(FocusedControlKind.Document, null, null, SelectionEditability.Unknown)]
    [InlineData(FocusedControlKind.Container, null, null, SelectionEditability.Unknown)]
    [InlineData(FocusedControlKind.Unknown, null, null, SelectionEditability.Unknown)]
    public void Classifies_the_focused_control(FocusedControlKind kind, bool? accessibilityReadOnly, bool? classicReadOnly, SelectionEditability expected)
    {
        Assert.Equal(expected, EditabilityRules.Classify(new FocusedControlFacts(kind, null, accessibilityReadOnly, classicReadOnly)));
    }

    [Theory]
    [InlineData(SelectionEditability.Editable, true)]
    [InlineData(SelectionEditability.Unknown, true)]
    [InlineData(SelectionEditability.ReadOnly, false)]
    public void Replace_is_tried_unless_known_read_only(SelectionEditability editability, bool expected)
    {
        Assert.Equal(expected, EditabilityRules.CanTryReplace(editability));
    }
}
