using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace AK.BuildingBlocks.Configuration;

// M3 Step 1 — the secret-less configuration foundation.
//
// This adds Azure Key Vault as a .NET *configuration source*. At startup the service reads
// its secrets (connection strings, API keys, …) from the vault BY NAME and folds them into
// IConfiguration — so the secret VALUES are never written into appsettings or committed.
//
// How the service authenticates to Key Vault: DefaultAzureCredential. It is a *credential
// chain* that resolves automatically to the correct identity for wherever the code runs:
//   • Locally  → the developer's Azure CLI sign-in (`az login`). That developer identity
//                must hold the "Key Vault Secrets User" role on the vault.
//   • In Azure → the resource's managed identity (an AKS workload identity, or a Function /
//                App Service system-assigned identity). Nothing is stored anywhere.
// Same code, no secrets — the environment decides which identity is used.
//
// IMPORTANT: this is the *service's own* identity authenticating to Key Vault. It is entirely
// separate from end-user authentication (the JWT a caller presents), which is handled by the
// identity-migration step — not here.
public static class KeyVaultConfigurationExtensions
{
    // Reads "KeyVault:Uri" from the configuration already loaded (appsettings / env vars).
    //   • Present → registers the Key Vault configuration provider, which loads every secret
    //               in the vault into IConfiguration using DefaultAzureCredential.
    //   • Absent  → does nothing, so the app still runs purely from local configuration
    //               (offline development, or a unit-test host with no vault).
    // The provider is deliberately *conditional*: the same build runs both with and without a
    // vault, and only the presence of the URI switches it on. The URI itself is non-secret.
    public static IConfigurationBuilder AddAzureKeyVaultConfiguration(
        this IConfigurationBuilder builder,
        IConfiguration configuration)
    {
        var keyVaultUri = configuration["KeyVault:Uri"];
        if (string.IsNullOrWhiteSpace(keyVaultUri))
            return builder; // No vault configured — keep running from local configuration only.

        // DefaultAzureCredential walks an ordered chain of credential sources and uses the
        // first that succeeds. With no options it already covers both target environments:
        //   • a managed identity / environment credentials (cloud)
        //   • the Azure CLI login (local development)
        //
        // For a *user-assigned* managed identity you would name it explicitly:
        //     new DefaultAzureCredential(new DefaultAzureCredentialOptions
        //     {
        //         ManagedIdentityClientId = "<user-assigned-identity-client-id>"
        //     });
        // The current setup — developer CLI locally, a system-assigned identity in the cloud —
        // needs no options, so the parameterless credential is used.
        var credential = new DefaultAzureCredential();

        builder.AddAzureKeyVault(new Uri(keyVaultUri), credential);
        return builder;
    }
}
