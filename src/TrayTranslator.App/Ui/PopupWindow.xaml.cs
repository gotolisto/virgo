using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using TrayTranslator.App.Interop;
using TrayTranslator.App.Platform;
using TrayTranslator.Core.Platform;
using TrayTranslator.Core.Popup;
using TrayTranslator.Core.Translation;

namespace TrayTranslator.App.Ui;

/// <summary>
/// The translation popup. Created once at startup and kept hidden, then reused for every Ctrl+C+C.
/// Positions itself in physical pixels next to the cursor, inside the cursor monitor's working area.
/// </summary>
public partial class PopupWindow : Window
{
    private readonly ICursorLocator _locator;
    private readonly DoubleAnimation _spin = new(0, 360, new Duration(TimeSpan.FromSeconds(0.9))) { RepeatBehavior = RepeatBehavior.Forever };
    private ScreenPoint _anchor;
    private PopupAction _action;

    internal PopupWindow(ICursorLocator locator)
    {
        _locator = locator;
        InitializeComponent();

        // Create the native window now so the first popup appears without the creation cost.
        _ = new WindowInteropHelper(this).EnsureHandle();

        SizeChanged += (_, _) => Reposition();
        Deactivated += (_, _) => HideAndReset();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public event EventHandler? SwapRequested;

    public event EventHandler? RetryRequested;

    public event EventHandler? OpenSettingsRequested;

    /// <summary>Raised with the text to put on the clipboard when Copy is pressed.</summary>
    public event EventHandler<string>? CopyRequested;

    private nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Shows the spinner at <paramref name="cursor"/>.</summary>
    public void ShowLoading(ScreenPoint cursor, string direction)
    {
        DirectionText.Text = direction;
        NoticeText.Visibility = Visibility.Collapsed;
        SetBody(loading: true);
        SwapButton.IsEnabled = false;
        ShowAt(cursor);
    }

    public void ShowTranslation(string direction, string text, string? notice)
    {
        DirectionText.Text = direction;
        SetNotice(notice);
        ResultText.Text = text;
        ResultText.ScrollToHome();
        SetBody(result: true);
        SwapButton.IsEnabled = true;
        CopyButton.Visibility = Visibility.Visible;
        SetAction(PopupAction.None, null);
    }

    public void ShowMessage(ScreenPoint? cursor, string direction, string message, PopupAction action, string? actionLabel, bool canSwap)
    {
        DirectionText.Text = direction;
        NoticeText.Visibility = Visibility.Collapsed;
        MessageText.Text = message;
        SetBody(message: true);
        SwapButton.IsEnabled = canSwap;
        CopyButton.Visibility = Visibility.Collapsed;
        SetAction(action, actionLabel);
        if (cursor is { } point)
        {
            ShowAt(point);
        }
    }

    public void HideAndReset()
    {
        if (!IsVisible)
        {
            return;
        }

        SpinnerRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 and similar must not destroy the reusable window.
        e.Cancel = true;
        HideAndReset();
        base.OnClosing(e);
    }

    private void ShowAt(ScreenPoint cursor)
    {
        _anchor = cursor;
        if (!IsVisible)
        {
            // Move onto the cursor's monitor first, so WPF picks up that monitor's DPI before layout.
            User32.SetWindowPos(Handle, 0, cursor.X, cursor.Y, 0, 0, User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
            Show();
        }

        UpdateLayout();
        Reposition();
        ForegroundHelper.Activate(Handle);
        Activate();
        Focus();
    }

    private void Reposition()
    {
        if (!IsVisible || !User32.GetWindowRect(Handle, out var rect))
        {
            return;
        }

        var monitor = _locator.GetMonitorAt(_anchor);
        var target = PopupPlacement.Compute(_anchor, rect.Right - rect.Left, rect.Bottom - rect.Top, monitor);
        if (target.X != rect.Left || target.Y != rect.Top)
        {
            User32.SetWindowPos(Handle, 0, target.X, target.Y, 0, 0, User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }
    }

    private void SetBody(bool loading = false, bool result = false, bool message = false)
    {
        LoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ResultText.Visibility = result ? Visibility.Visible : Visibility.Collapsed;
        MessageText.Visibility = message ? Visibility.Visible : Visibility.Collapsed;
        if (loading)
        {
            CopyButton.Visibility = Visibility.Collapsed;
            SetAction(PopupAction.None, null);
            SpinnerRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, _spin);
        }
        else
        {
            SpinnerRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        }
    }

    private void SetNotice(string? notice)
    {
        NoticeText.Text = notice ?? string.Empty;
        NoticeText.Visibility = string.IsNullOrEmpty(notice) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetAction(PopupAction action, string? label)
    {
        _action = action;
        ActionButton.Content = label;
        ActionButton.Visibility = action == PopupAction.None ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideAndReset();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => HideAndReset();

    private void OnSwapClick(object sender, RoutedEventArgs e) => SwapRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        CopyRequested?.Invoke(this, ResultText.Text);
        HideAndReset();
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        switch (_action)
        {
            case PopupAction.Retry:
                RetryRequested?.Invoke(this, EventArgs.Empty);
                break;
            case PopupAction.OpenSettings:
                HideAndReset();
                OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
