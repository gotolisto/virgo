namespace TrayTranslator.Core.Platform;

/// <summary>One entry in the tray menu. <see cref="Separator"/> draws a divider line.</summary>
public sealed record TrayMenuItem(
    string Text,
    Action? OnClick = null,
    bool IsEnabled = true,
    bool IsChecked = false)
{
    public static TrayMenuItem Separator { get; } = new("-") { IsSeparator = true };

    public bool IsSeparator { get; private init; }
}

/// <summary>The notification-area icon and its menu.</summary>
public interface ITrayIcon : IDisposable
{
    /// <summary>Raised just before the menu opens, so the owner can refresh it with <see cref="SetMenu"/>.</summary>
    event EventHandler? MenuOpening;

    void Show();

    void SetTooltip(string text);

    void SetMenu(IReadOnlyList<TrayMenuItem> items);

    /// <summary>Shows a notification. When <paramref name="onClick"/> is given, clicking the notification runs it.</summary>
    void ShowNotification(string title, string message, Action? onClick = null);
}
