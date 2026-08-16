using AK.BuildingBlocks.Configuration;
using AK.Notification.Core.Extensions;
using Microsoft.Extensions.Hosting;

// .NET 9 isolated-worker host for the AntKart notification Functions app
// (deployed to the provisioned Function App "func-antkart-notifications-dev").
//
// The Function App runs with a MANAGED IDENTITY — there are no secrets in configuration. Two things
// are wired here:
//   • Key Vault as a configuration source (when KeyVault:Uri is set), via DefaultAzureCredential, so
//     the ACS settings and the Notifications DB connection string are read secret-less (no-op locally
//     when KeyVault:Uri is absent — same pattern as the other services).
//   • AddNotificationCore — the ONE call that wires the reusable core: the dispatcher, the Email
//     channel (ACS, managed identity), the per-type templates, and the EF history store + DbContext.
// The Functions themselves stay thin (see NotificationFunctions) — they only trigger and dispatch.
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((context, config) =>
        config.AddAzureKeyVaultConfiguration(context.Configuration))
    .ConfigureServices((context, services) =>
        services.AddNotificationCore(context.Configuration))
    .Build();

host.Run();
