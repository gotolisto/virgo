using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using TrayTranslator.App.Interop;
using TrayTranslator.App.Platform;
using TrayTranslator.Core.AiActions;
using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Platform;
using TrayTranslator.Core.Popup;
using TrayTranslator.Core.Translation;

namespace TrayTranslator.App.Ui;

/// <summary>
/// The popup. Created once at startup and kept hidden, then reused for every Ctrl+C+C and AI action.
/// Positions itself in physical pixels next to the cursor, inside the cursor monitor's working area.
/// <para>
/// The translator and the AI actions share it: whoever handles a gesture first calls <see cref="Claim"/>, and
/// only the current claimant acts on the popup's buttons.
/// </para>
/// </summary>
public partial class PopupWindow : Window
{
    private readonly ICursorLocator _locator;
    private readonly DoubleAnimation _spin = new(0, 360, new Duration(TimeSpan.FromSeconds(0.9))) { RepeatBehavior = RepeatBehavior.Forever };
    private ScreenPoint _anchor;
    private PopupAction _action;
    private IReadOnlyList<Language> _choices = [];
    private object? _claimant;

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

    /// <summary>Raised when a different claimant takes the popup over; the previous one should drop its work.</summary>
    public event EventHandler? Claimed;

    public event EventHandler? SwapRequested;

    public event EventHandler? RetryRequested;

    public event EventHandler? OpenSettingsRequested;

    /// <summary>Raised with the text to put on the clipboard when Copy is pressed.</summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>Raised when a language is picked in the header's language menu.</summary>
    public event EventHandler<Language>? TargetPicked;

    /// <summary>Raised with the translation when Replace is pressed (after the popup has hidden itself).</summary>
    public event EventHandler<string>? ReplaceRequested;

    private nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Makes <paramref name="claimant"/> the one the popup's buttons are for.</summary>
    public void Claim(object claimant)
    {
        if (ReferenceEquals(_claimant, claimant))
        {
            return;
        }

        _claimant = claimant;
        Claimed?.Invoke(this, EventArgs.Empty);
    }

    public bool IsClaimedBy(object claimant) => ReferenceEquals(_claimant, claimant);

    /// <summary>The languages offered in the header's menu, in order.</summary>
    public void SetChoices(IReadOnlyList<Language> choices) => _choices = choices;

    /// <summary>Shows the spinner at <paramref name="cursor"/>; <paramref name="direction"/> is null while unknown.</summary>
    public void ShowLoading(ScreenPoint cursor, TranslationDirection? direction)
    {
        SetMode(ai: false);
        SetHeader(direction?.Source, direction?.Target, pickable: false);
        NoticeText.Visibility = Visibility.Collapsed;
        SetBody(loading: true);
        SwapButton.IsEnabled = false;
        ShowAt(cursor);
    }

    /// <param name="canReplace">Show the Replace button (there is a window to paste into).</param>
    /// <param name="cursor">Show the popup at this point first; null when it is already showing.</param>
    public void ShowTranslation(Language? source, Language target, string text, string? notice, bool canReplace, ScreenPoint? cursor = null)
    {
        SetMode(ai: false);
        SetHeader(source, target, pickable: true);
        SetNotice(notice);
        ResultText.Text = text;
        ResultText.ScrollToHome();
        SetBody(result: true);
        SwapButton.IsEnabled = true;
        CopyButton.Visibility = Visibility.Visible;
        ReplaceButton.Visibility = canReplace ? Visibility.Visible : Visibility.Collapsed;
        SetAction(PopupAction.None, null);
        if (cursor is { } point)
        {
            ShowAt(point);
        }
    }

    /// <param name="direction">The direction the failed request used; null when there was none (e.g. too long).</param>
    public void ShowMessage(ScreenPoint? cursor, TranslationDirection? direction, string message, PopupAction action, string? actionLabel, bool canSwap)
    {
        SetMode(ai: false);
        SetHeader(direction?.Source, direction?.Target, pickable: direction is not null, emptyWhenUnknown: direction is null);
        NoticeText.Visibility = Visibility.Collapsed;
        MessageText.Text = message;
        SetBody(message: true);
        SwapButton.IsEnabled = canSwap;
        CopyButton.Visibility = Visibility.Collapsed;
        ReplaceButton.Visibility = Visibility.Collapsed;
        SetAction(action, actionLabel);
        if (cursor is { } point)
        {
            ShowAt(point);
        }
    }

    /// <summary>An AI action is running: its name in the header and the spinner.</summary>
    public void ShowAiWorking(ScreenPoint cursor, string title)
    {
        SetMode(ai: true);
        SetTitle(title);
        NoticeText.Visibility = Visibility.Collapsed;
        SetBody(loading: true);
        ShowAt(cursor);
    }

    /// <summary>An AI action's result, with Copy (and Replace when <paramref name="canReplace"/>).</summary>
    /// <param name="cursor">Show the popup at this point first; null when it is already showing.</param>
    public void ShowAiResult(string title, string text, string? notice, bool canReplace, ScreenPoint? cursor = null)
    {
        SetMode(ai: true);
        SetTitle(title);
        SetNotice(notice);
        ResultText.Text = text;
        ResultText.ScrollToHome();
        SetBody(result: true);
        CopyButton.Visibility = Visibility.Visible;
        ReplaceButton.Visibility = canReplace ? Visibility.Visible : Visibility.Collapsed;
        SetAction(PopupAction.None, null);
        if (cursor is { } point)
        {
            ShowAt(point);
        }
    }

    /// <summary>Why an AI action did not finish, with a button for what to do about it.</summary>
    /// <param name="cursor">Show the popup at this point first; null when it is already showing.</param>
    public void ShowAiMessage(ScreenPoint? cursor, string title, PopupMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        SetMode(ai: true);
        SetTitle(title);
        NoticeText.Visibility = Visibility.Collapsed;
        MessageText.Text = message.Message;
        SetBody(message: true);
        CopyButton.Visibility = Visibility.Collapsed;
        ReplaceButton.Visibility = Visibility.Collapsed;
        SetAction(message.Action, message.ActionLabel);
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

        TargetButton.IsChecked = false;
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
            ReplaceButton.Visibility = Visibility.Collapsed;
            SetAction(PopupAction.None, null);
            SpinnerRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, _spin);
        }
        else
        {
            SpinnerRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        }
    }

    /// <summary>Translation shows languages and the swap button; an AI action shows its name instead.</summary>
    private void SetMode(bool ai)
    {
        SwapButton.Visibility = ai ? Visibility.Collapsed : Visibility.Visible;
        LoadingText.Text = ai ? AiPopupText.Working : "Translating…";
        ReplaceButton.ToolTip = ai ? "Paste the result over the text you selected" : "Paste the translation over the text you selected";
        if (ai)
        {
            TargetButton.IsChecked = false;
            TargetButton.Visibility = Visibility.Collapsed;
        }
    }

    private void SetTitle(string title) => DirectionText.Text = $"✦ {title}";

    /// <summary>"ES →" plus the target as a button that opens the language menu.</summary>
    private void SetHeader(Language? source, Language? target, bool pickable, bool emptyWhenUnknown = false)
    {
        TargetButton.IsChecked = false;
        if (target is null)
        {
            DirectionText.Text = emptyWhenUnknown ? string.Empty : "…";
            TargetButton.Visibility = Visibility.Collapsed;
            return;
        }

        DirectionText.Text = $"{source?.Abbreviation ?? "Auto"} →";
        TargetText.Text = target.Abbreviation;
        TargetButton.Visibility = Visibility.Visible;
        TargetButton.IsEnabled = pickable && _choices.Count > 1;

        TargetList.Children.Clear();
        foreach (var language in _choices)
        {
            var row = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("MenuRow"),
                Content = language.Name,
                FontWeight = language.Code == target.Code ? FontWeights.SemiBold : FontWeights.Normal,
            };
            row.Click += (_, _) =>
            {
                TargetButton.IsChecked = false;
                TargetPicked?.Invoke(this, language);
            };
            TargetList.Children.Add(row);
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

    private void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        var text = ResultText.Text;

        // Hide first, so the original window can take focus back.
        HideAndReset();
        ReplaceRequested?.Invoke(this, text);
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
