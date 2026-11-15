using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrayTranslator.Core.Engines;

namespace TrayTranslator.App.Ui;

/// <summary>A masked API key box with a Test button that checks the key without spending characters.</summary>
public partial class ApiKeyField : UserControl
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private ITranslationEngine? _engine;
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

    internal void Configure(string label, ITranslationEngine engine, string signUpUrl)
    {
        LabelText.Text = label;
        _engine = engine;
        _signUpUrl = signUpUrl;
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
        if (_engine is null)
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
            await _engine.TestKeyAsync(Key, cts.Token);
            ShowStatus("Key works", null, Brushes.SeaGreen);
            SetVerified(true);
        }
        catch (TranslationException ex)
        {
            ShowStatus(ex.Kind switch
            {
                TranslationErrorKind.MissingKey => "Enter a key first",
                TranslationErrorKind.BadKey when _engine.Id == Core.Settings.EngineId.Google =>
                    "Key not accepted. Check the key and that the Cloud Translation API is enabled for its project",
                TranslationErrorKind.BadKey => "Key not accepted. Check the key",
                TranslationErrorKind.NoConnection => "No connection",
                TranslationErrorKind.RateLimited => "Too many requests. Try again in a moment",
                TranslationErrorKind.QuotaExceeded => "Key works, but this month's allowance is used up",
                _ => "Could not check the key. Try again",
            }, null, Brushes.IndianRed);
            SetVerified(ex.Kind == TranslationErrorKind.QuotaExceeded);
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
