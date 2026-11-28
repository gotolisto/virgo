using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrayTranslator.Core.AiActions;
using TrayTranslator.Core.Engines;

namespace TrayTranslator.App.Ui;

/// <param name="Works">The key can be used: show the message in green.</param>
/// <param name="Message">What the test found.</param>
/// <param name="Verified">Counts as a working key (a used-up allowance still does).</param>
internal sealed record KeyCheck(bool Works, string Message, bool Verified);

/// <summary>Checks a key without spending anything. Throws only <see cref="OperationCanceledException"/>.</summary>
internal delegate Task<KeyCheck> KeyTester(string key, CancellationToken ct);

/// <summary>A masked API key box with a Test button that checks the key without spending characters.</summary>
public partial class ApiKeyField : UserControl
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private KeyTester? _tester;
    private string _signUpUrl = string.Empty;
    private CancellationTokenSource? _test;

    public ApiKeyField()
    {
        InitializeComponent();
    }

    /// <summary>Raised when a test result changes whether the current key is known to work.</summary>
    public event EventHandler? VerifiedChanged;

    public string Key
    {
        get => KeyBox.Password.Trim();
        set => KeyBox.Password = value ?? string.Empty;
    }

    /// <summary>True when the key in the box passed the last test.</summary>
    public bool IsVerified { get; private set; }

    internal void Configure(string label, ITranslationEngine engine, string signUpUrl) =>
        Configure(label, (key, ct) => TestEngineAsync(engine, key, ct), signUpUrl);

    internal void Configure(string label, IAiRewriter rewriter, string signUpUrl) =>
        Configure(label, (key, ct) => TestClaudeAsync(rewriter, key, ct), signUpUrl);

    internal void Configure(string label, KeyTester tester, string signUpUrl)
    {
        LabelText.Text = label;
        _tester = tester;
        _signUpUrl = signUpUrl;
    }

    private static async Task<KeyCheck> TestEngineAsync(ITranslationEngine engine, string key, CancellationToken ct)
    {
        try
        {
            await engine.TestKeyAsync(key, ct);
            return new KeyCheck(true, "Key works", true);
        }
        catch (TranslationException ex)
        {
            var message = ex.Kind switch
            {
                TranslationErrorKind.MissingKey => "Enter a key first",
                TranslationErrorKind.BadKey when engine.Id == Core.Settings.EngineId.Google =>
                    "Key not accepted. Check the key and that the Cloud Translation API is enabled for its project",
                TranslationErrorKind.BadKey => "Key not accepted. Check the key",
                TranslationErrorKind.NoConnection => "No connection",
                TranslationErrorKind.RateLimited => "Too many requests. Try again in a moment",
                TranslationErrorKind.QuotaExceeded => "Key works, but this month's allowance is used up",
                _ => "Could not check the key. Try again",
            };
            return new KeyCheck(false, message, ex.Kind == TranslationErrorKind.QuotaExceeded);
        }
    }

    private static async Task<KeyCheck> TestClaudeAsync(IAiRewriter rewriter, string key, CancellationToken ct)
    {
        try
        {
            await rewriter.TestKeyAsync(key, ct);
            return new KeyCheck(true, "Key works", true);
        }
        catch (AiException ex)
        {
            var message = ex.Kind switch
            {
                AiErrorKind.MissingKey => "Enter a key first",
                AiErrorKind.BadKey => "Key not accepted. Check the key",
                AiErrorKind.NoConnection => "No connection",
                AiErrorKind.RateLimited => "Too many requests. Try again in a moment",
                AiErrorKind.BadRequest when ex.Detail is { } detail => $"Could not check the key: {detail}",
                _ => "Could not check the key. Try again",
            };
            return new KeyCheck(false, message, false);
        }
    }

    private void OnSignUpClick(object sender, RoutedEventArgs e) => Links.Open(_signUpUrl);

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _test?.Cancel();
        StatusText.Visibility = Visibility.Collapsed;
        SetVerified(false);
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (_tester is null)
        {
            return;
        }

        _test?.Cancel();
        using var cts = new CancellationTokenSource(TestTimeout);
        _test = cts;
        TestButton.IsEnabled = false;
        ShowStatus("Testing…", "Theme.Secondary");

        try
        {
            var check = await _tester(Key, cts.Token);
            ShowStatus(check.Message, null, check.Works ? Brushes.SeaGreen : Brushes.IndianRed);
            SetVerified(check.Verified);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_test, cts))
            {
                ShowStatus("No answer. Try again", null, Brushes.IndianRed);
            }
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string text, string? brushKey, Brush? brush = null)
    {
        StatusText.Text = text;
        StatusText.Foreground = brush ?? (brushKey is null ? Brushes.Gray : (Brush)FindResource(brushKey));
        StatusText.Visibility = Visibility.Visible;
    }

    private void SetVerified(bool verified)
    {
        if (IsVerified == verified)
        {
            return;
        }

        IsVerified = verified;
        VerifiedChanged?.Invoke(this, EventArgs.Empty);
    }
}
