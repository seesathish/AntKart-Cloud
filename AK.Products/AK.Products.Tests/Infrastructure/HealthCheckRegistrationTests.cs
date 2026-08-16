using AK.BuildingBlocks.HealthChecks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AK.Products.Tests.Infrastructure;

public sealed class HealthCheckRegistrationTests
{
    private static IReadOnlyList<HealthCheckRegistration> Registrations(IServiceCollection services) =>
        services.BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.ToList();

    [Fact]
    public void AddDefaultHealthChecks_RegistersShallowSelf_TaggedLiveAndReady()
    {
        var services = new ServiceCollection();
        services.AddDefaultHealthChecks();

        var self = Registrations(services).Single(r => r.Name == "self");
        self.Tags.Should().Contain(HealthCheckTags.Live);
        self.Tags.Should().Contain(HealthCheckTags.Ready);
        self.Tags.Should().NotContain(HealthCheckTags.Deep);
    }

    [Fact]
    public void DeepCheck_IsTaggedDeepOnly_NotLiveOrReady()
    {
        // A deep dependency check must never carry Live/Ready tags, or it would leak onto the
        // liveness/readiness probes and risk a restart storm / fleet-wide blackout.
        var services = new ServiceCollection();
        services.AddDefaultHealthChecks()
            .AddCheck("cosmos", () => HealthCheckResult.Healthy(), tags: new[] { HealthCheckTags.Deep });

        var deep = Registrations(services).Single(r => r.Name == "cosmos");
        deep.Tags.Should().Contain(HealthCheckTags.Deep);
        deep.Tags.Should().NotContain(HealthCheckTags.Live);
        deep.Tags.Should().NotContain(HealthCheckTags.Ready);
    }

    [Fact]
    public void AddKeyVaultDeepCheck_RegistersKeyVault_TaggedDeep()
    {
        var services = new ServiceCollection();
        services.AddDefaultHealthChecks().AddKeyVaultDeepCheck();

        var kv = Registrations(services).Single(r => r.Name == "keyvault");
        kv.Tags.Should().Contain(HealthCheckTags.Deep);
        kv.Tags.Should().NotContain(HealthCheckTags.Live);
    }
}
