using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class KoFiHostedRelayConsentTests
{
    [Fact]
    public void NewSettings_DefaultToLocalWebhook()
    {
        var settings = new CashPaymentConnectionSettings();

        Assert.Equal(KoFiConnectionMode.LocalWebhook, settings.KoFiConnectionMode);
        Assert.False(settings.KoFiUseHostedRelay);
        Assert.False(settings.KoFiHostedRelayWarningAccepted);
    }

    [Fact]
    public void HostedRelay_WithoutConsent_StaysLocal()
    {
        var settings = new CashPaymentConnectionSettings
        {
            KoFiConnectionMode = KoFiConnectionMode.HostedRelay
        };

        Assert.Equal(KoFiConnectionMode.LocalWebhook, settings.KoFiConnectionMode);

        settings.KoFiUseHostedRelay = true;
        Assert.False(settings.KoFiUseHostedRelay);
    }

    [Fact]
    public void HostedRelay_WithConsent_TurnsOn()
    {
        var settings = new CashPaymentConnectionSettings
        {
            KoFiHostedRelayWarningAccepted = true,
            KoFiConnectionMode = KoFiConnectionMode.HostedRelay
        };

        Assert.True(settings.KoFiUseHostedRelay);
    }

    [Fact]
    public void TurningHostedRelayOff_ClearsConsent()
    {
        var settings = new CashPaymentConnectionSettings { KoFiHostedRelayWarningAccepted = true };
        settings.KoFiUseHostedRelay = true;

        settings.KoFiUseHostedRelay = false;

        Assert.Equal(KoFiConnectionMode.LocalWebhook, settings.KoFiConnectionMode);
        Assert.False(settings.KoFiHostedRelayWarningAccepted);
    }

    [Fact]
    public void RevokingConsent_SwitchesBackToLocal()
    {
        var settings = new CashPaymentConnectionSettings { KoFiHostedRelayWarningAccepted = true };
        settings.KoFiUseHostedRelay = true;

        settings.KoFiHostedRelayWarningAccepted = false;

        Assert.Equal(KoFiConnectionMode.LocalWebhook, settings.KoFiConnectionMode);
    }
}
