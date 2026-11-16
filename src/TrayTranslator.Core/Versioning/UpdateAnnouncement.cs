namespace TrayTranslator.Core.Versioning;

public static class UpdateAnnouncement
{
    /// <summary>
    /// True when the app has just been updated: setup is done (so this is not a fresh install) and the
    /// version last seen is older than <paramref name="current"/>, or was never recorded (an update from a
    /// build that predates this check).
    /// </summary>
    public static bool ShouldAnnounce(string? lastSeenVersion, AppVersion current, bool firstRunCompleted)
    {
        if (!firstRunCompleted)
        {
            return false;
        }

        return !AppVersion.TryParse(lastSeenVersion, out var lastSeen) || lastSeen < current;
    }
}
