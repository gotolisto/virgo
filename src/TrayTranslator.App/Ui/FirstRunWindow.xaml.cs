using System.Windows;
using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Secrets;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.App.Ui;

/// <summary>
/// Shown until setup is finished: the language pair and at least one tested key. Finishing saves both and
/// leaves the app in the tray.
/// </summary>
public partial class FirstRunWindow : Window
{
    private readonly WindowContext _context;

    internal FirstRunWindow(WindowContext context)
    {
        _context = context;
        InitializeComponent();
        ThemeMode = context.IsDarkTheme() ? System.Windows.ThemeMode.Dark : System.Windows.ThemeMode.Light;

        LanguageA.ItemsSource = LanguageCatalog.All;
        LanguageB.ItemsSource = LanguageCatalog.All;
        var s = context.Settings.Current;
        LanguageA.SelectedValue = s.LanguageA;
        LanguageB.SelectedValue = s.LanguageB;

        DeepLKey.Configure("DeepL API Free key", context.Engines[EngineId.DeepL], Links.DeepLSignUp);
        GoogleKey.Configure("Google Cloud Translation API key", context.Engines[EngineId.Google], Links.GoogleSignUp);
        DeepLKey.Key = context.Secrets.Get(SecretNames.DeepLApiKey) ?? string.Empty;
        GoogleKey.Key = context.Secrets.Get(SecretNames.GoogleApiKey) ?? string.Empty;

        DeepLKey.VerifiedChanged += (_, _) => UpdateFinish();
        GoogleKey.VerifiedChanged += (_, _) => UpdateFinish();
        LanguageA.SelectionChanged += (_, _) => UpdateFinish();
        LanguageB.SelectionChanged += (_, _) => UpdateFinish();
    }

    private void UpdateFinish()
    {
        var languagesOk = LanguageA.SelectedValue is string a && LanguageB.SelectedValue is string b && a != b;
        var keyOk = DeepLKey.IsVerified || GoogleKey.IsVerified;
        FinishButton.IsEnabled = languagesOk && keyOk;
        HintText.Text = !languagesOk ? "Choose two different languages." : keyOk ? "Ready." : "Test a key to continue.";
    }

    private void OnFinishClick(object sender, RoutedEventArgs e)
    {
        // Only keys that passed their test are kept.
        _context.Secrets.Set(SecretNames.DeepLApiKey, DeepLKey.IsVerified ? DeepLKey.Key : null);
        _context.Secrets.Set(SecretNames.GoogleApiKey, GoogleKey.IsVerified ? GoogleKey.Key : null);

        var defaultEngine = DeepLKey.IsVerified ? EngineId.DeepL : EngineId.Google;
        _context.Settings.Save(_context.Settings.Current with
        {
            LanguageA = (string)LanguageA.SelectedValue,
            LanguageB = (string)LanguageB.SelectedValue,
            DefaultEngine = defaultEngine,
            FirstRunCompleted = true,
        });

        Close();
    }
}
