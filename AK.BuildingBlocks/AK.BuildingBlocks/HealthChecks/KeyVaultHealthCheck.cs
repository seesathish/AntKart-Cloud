using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AK.BuildingBlocks.HealthChecks;

// DEEP diagnostic check: can this service reach Azure Key Vault with its OWN identity?
//
// It lists secret PROPERTIES (names / metadata only — never secret VALUES) as a cheap reachability
// + authorization probe. Registered with the Deep tag, so — like every deep check — it is exposed
// ONLY on the diagnostic endpoint (/health/deps) and can never restart or de-register a pod.
public sealed class KeyVaultHealthCheck : IHealthCheck
{
    private readonly string? _vaultUri;

    public KeyVaultHealthCheck(IConfiguration configuration)
        => _vaultUri = configuration["KeyVault:Uri"];

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // No vault configured (offline / local dev) ⇒ nothing to reach. Report Healthy so the
        // diagnostic view is not misleadingly red where Key Vault is intentionally absent.
        if (string.IsNullOrWhiteSpace(_vaultUri))
            return HealthCheckResult.Healthy("Key Vault not configured (local).");

        try
        {
            var client = new SecretClient(new Uri(_vaultUri), new DefaultAzureCredential());

            // Pull a single page of secret metadata: proves auth + reachability, reads no value.
            await foreach (var _ in client.GetPropertiesOfSecretsAsync(cancellationToken))
                break;

            return HealthCheckResult.Healthy("Key Vault reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Key Vault unreachable.", ex);
        }
    }
}

public static class KeyVaultHealthCheckExtensions
{
    // Opt-in: a service that loads secrets from Key Vault adds this DEEP check to its diagnostic
    // surface. It is never tagged Live/Ready, so it cannot affect the liveness/readiness probes.
    public static IHealthChecksBuilder AddKeyVaultDeepCheck(
        this IHealthChecksBuilder builder, string name = "keyvault")
        => builder.AddCheck<KeyVaultHealthCheck>(name, tags: new[] { HealthCheckTags.Deep });
}
