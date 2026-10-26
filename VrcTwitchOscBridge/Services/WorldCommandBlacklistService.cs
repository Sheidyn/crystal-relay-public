using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

internal enum WorldCommandBlacklistRefreshStatus
{
    Disabled,
    Ready,
    RequestFailed,
    InvalidResponse
}

internal sealed record WorldCommandBlacklistRefreshResult(
    WorldCommandBlacklistRefreshStatus Status,
    int WorldEntryCount = 0,
    int CreatorEntryCount = 0,
    DateTimeOffset? UpdatedAtUtc = null);

internal sealed record WorldCommandBlacklistDecision(bool IsBlocked, bool IsFailClosed)
{
    public static WorldCommandBlacklistDecision Allow { get; } = new(false, false);

    public static WorldCommandBlacklistDecision Block(bool isFailClosed = false) =>
        new(true, isFailClosed);
}

// The remote world guard has been removed: world sharing is decided locally and
// no world, creator, or app information is sent anywhere. VrChatApiClient still
// only returns public worlds for the world command.
internal sealed class WorldCommandBlacklistService : IDisposable
{
    public void Configure(WorldCommandBlacklistSettings settings) => _ = settings;

    public void InvalidateCurrentSessionList()
    {
    }

    public Task<WorldCommandBlacklistDecision> EvaluateAsync(
        string worldId,
        string authorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WorldCommandBlacklistDecision.Allow);
    }

    public Task<WorldCommandBlacklistRefreshResult> RefreshAsync(
        WorldCommandBlacklistSettings settings,
        bool force,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new WorldCommandBlacklistRefreshResult(WorldCommandBlacklistRefreshStatus.Disabled));
    }

    public void Dispose()
    {
    }
}
