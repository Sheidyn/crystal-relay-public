using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class WorldCommandBlacklistSettings : ObservableObject
{
    /// <summary>
    /// Kept for saved-settings compatibility. The remote world guard has been removed,
    /// so this no longer contacts any server.
    /// </summary>
    public bool IsEnabled
    {
        get => true;
        set { /* No remote guard to toggle. */ }
    }
}
