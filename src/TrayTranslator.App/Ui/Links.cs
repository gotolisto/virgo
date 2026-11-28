using System.Diagnostics;

namespace TrayTranslator.App.Ui;

internal static class Links
{
    /// <summary>DeepL API Free sign-up (the plan picker on the DeepL API page).</summary>
    public const string DeepLSignUp = "https://www.deepl.com/pro-api";

    /// <summary>Google Cloud: enable Cloud Translation, then create an API key under Credentials.</summary>
    public const string GoogleSignUp = "https://console.cloud.google.com/apis/library/translate.googleapis.com";

    /// <summary>Claude Console: create an API key (sign up first if needed).</summary>
    public const string ClaudeSignUp = "https://platform.claude.com/settings/keys";

    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No default browser; nothing useful to do.
        }
    }
}
