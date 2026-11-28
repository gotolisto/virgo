using System.Globalization;
using TrayTranslator.Core.Input;
using TrayTranslator.Core.Platform;
using TrayTranslator.Core.Translation;

namespace TrayTranslator.Core.AiActions;

/// <summary>Every user-facing popup string for AI actions, in one place.</summary>
public static class AiPopupText
{
    public const string Working = "Working…";
    public const string AddKey = "Add your Claude API key in Settings to use AI actions";
    public const string CheckKey = "Claude did not accept the API key. Check it in Settings";
    public const string RateLimited = "Claude is busy with too many requests. Try again in a moment";
    public const string NoAnswer = "Claude did not answer in time";
    public const string ServerError = "Claude is having a temporary problem";
    public const string NoConnection = "No connection";
    public const string Refused = "Claude declined to work on this text";
    public const string TooLongAnswer = "The result was too long to finish, so nothing was replaced";
    public const string EmptyAnswer = "Claude returned no text";
    public const string KeysHeld = "Let go of the keys after the second press, then try again";
    public const string CannotCopy = "Could not copy the selected text from that window";

    public static string NothingSelected(Shortcut shortcut) =>
        $"Select some text first, then press {shortcut.ToGestureString()}";

    public static string TooLong(int limit) =>
        string.Create(CultureInfo.InvariantCulture, $"Too long: the limit is {limit:N0} characters");

    public static PopupMessage For(SelectionCopy copy, Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(copy);
        return copy.Status switch
        {
            SelectionCopyStatus.NothingSelected => new PopupMessage(NothingSelected(shortcut)),
            SelectionCopyStatus.KeysHeld => new PopupMessage(KeysHeld),
            _ => new PopupMessage(CannotCopy),
        };
    }

    public static PopupMessage For(AiException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Kind switch
        {
            AiErrorKind.MissingKey => new PopupMessage(AddKey, PopupAction.OpenSettings, "Open Settings"),
            AiErrorKind.BadKey => new PopupMessage(CheckKey, PopupAction.OpenSettings, "Open Settings"),
            AiErrorKind.RateLimited => new PopupMessage(RateLimited, PopupAction.Retry, "Try again"),
            AiErrorKind.Timeout => new PopupMessage(NoAnswer, PopupAction.Retry, "Retry"),
            AiErrorKind.ServerError => new PopupMessage(ServerError, PopupAction.Retry, "Retry"),
            AiErrorKind.NoConnection => new PopupMessage(NoConnection, PopupAction.Retry, "Retry"),
            AiErrorKind.Refused => new PopupMessage(Refused),
            AiErrorKind.TooLong => new PopupMessage(TooLongAnswer),
            AiErrorKind.EmptyAnswer => new PopupMessage(EmptyAnswer, PopupAction.Retry, "Retry"),
            _ => new PopupMessage(error.Detail is { } detail ? $"Claude refused the request: {detail}" : "Claude refused the request"),
        };
    }
}
