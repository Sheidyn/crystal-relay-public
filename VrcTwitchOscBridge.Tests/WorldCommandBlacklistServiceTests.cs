using System.Net.Http;
using System.Reflection;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class WorldCommandBlacklistServiceTests
{
    private const string WorldId = "wrld_11111111-2222-3333-4444-555555555555";
    private const string AuthorId = "usr_aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    [Fact]
    public void Service_HasNoHttpClient()
    {
        var fields = typeof(WorldCommandBlacklistService).GetFields(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(fields, field => typeof(HttpClient).IsAssignableFrom(field.FieldType));
    }

    [Fact]
    public async Task Evaluate_AllowsLocallyWithoutFailClosed()
    {
        using var service = new WorldCommandBlacklistService();

        var decision = await service.EvaluateAsync(WorldId, AuthorId);

        Assert.False(decision.IsBlocked);
        Assert.False(decision.IsFailClosed);
    }

    [Fact]
    public async Task Refresh_ReportsDisabled()
    {
        using var service = new WorldCommandBlacklistService();

        var result = await service.RefreshAsync(new WorldCommandBlacklistSettings(), force: true);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.Disabled, result.Status);
    }

    [Fact]
    public async Task Evaluate_CallerCancellationPropagates()
    {
        using var service = new WorldCommandBlacklistService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.EvaluateAsync(WorldId, AuthorId, cancellation.Token));
    }
}
