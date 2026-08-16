using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AK.BuildingBlocks.Email;

// Non-secret configuration for ACS Email. The endpoint and sender address come from the
// infrastructure outputs (communication_service_endpoint and sender_address) and are committed.
// ConnectionString is the OPTIONAL fallback — it is a SECRET and, if ever used, is sourced from
// Key Vault at runtime; it is never written into appsettings or committed.
public sealed class AcsEmailOptions
{
    public string? Endpoint { get; set; }          // https://<resource>.<region>.communication.azure.com
    public string? SenderAddress { get; set; }      // DoNotReply@<managed-subdomain>.azurecomm.net
    public string? SenderDisplayName { get; set; }  // optional friendly From name, e.g. "AntKart"
    public string? ConnectionString { get; set; }   // optional Key-Vault fallback (secret) — not committed
}

// Azure Communication Services (ACS) Email implementation of IEmailSender.
//
// The EmailClient is built once (in AddAcsEmailSender) and injected here, which keeps the send
// logic free of any client-construction/auth concern and makes it unit-testable with a mocked
// EmailClient. Auth selection happens in the DI extension below.
public sealed class AcsEmailSender : IEmailSender
{
    private readonly EmailClient? _client;
    private readonly string? _senderAddress;
    private readonly ILogger<AcsEmailSender> _logger;

    public AcsEmailSender(EmailClient? client, IOptions<AcsEmailOptions> options, ILogger<AcsEmailSender> logger)
    {
        _client = client;
        _logger = logger;

        var o = options.Value;

        // A friendly From name is applied with the RFC 5322 "Display Name <address>" form so
        // recipients see e.g. "AntKart" rather than the bare DoNotReply@... address. With no
        // display name configured, the bare sender address is used.
        if (string.IsNullOrWhiteSpace(o.SenderAddress))
            _senderAddress = null;
        else if (string.IsNullOrWhiteSpace(o.SenderDisplayName))
            _senderAddress = o.SenderAddress;
        else
            _senderAddress = $"{o.SenderDisplayName} <{o.SenderAddress}>";
    }

    public async Task SendAsync(
        string to, string subject, string? htmlBody, string? plainTextBody = null, CancellationToken ct = default)
    {
        // SAFE NO-OP when ACS is not configured (e.g. local development without an endpoint).
        // This mirrors the Event Grid side-effect publisher: a missing dependency disables the
        // feature rather than breaking the caller. Real send failures DO propagate (below), so the
        // caller — the notification handler / Function — can record the failure; both wrap the call.
        if (_client is null || string.IsNullOrWhiteSpace(_senderAddress))
        {
            _logger.LogWarning(
                "ACS email is not configured; skipping send to {Recipient} (subject '{Subject}').", to, subject);
            return;
        }

        var content = new EmailContent(subject)
        {
            Html = htmlBody,
            PlainText = plainTextBody ?? string.Empty
        };
        var message = new EmailMessage(_senderAddress, to, content);

        // WaitUntil.Started submits the message and returns without polling for final delivery —
        // right for a fire-and-forget notification (we do not block the caller on the mail queue).
        await _client.SendAsync(WaitUntil.Started, message, ct);

        _logger.LogInformation("ACS email submitted to {Recipient} (subject '{Subject}').", to, subject);
    }
}

public static class AcsEmailSenderExtensions
{
    // Registers the ACS email sender. Reads non-secret config from the "Acs" section.
    //
    // AUTH SELECTION — Entra-first (secret-less), Key-Vault connection-string fallback:
    //   * If Acs:ConnectionString is present (it would be supplied from Key Vault, never committed),
    //     the client is built from it — the documented fallback for environments where Entra-based
    //     send is not available.
    //   * OTHERWISE (the default path) the client is built from Acs:Endpoint + DefaultAzureCredential,
    //     so it authenticates with the app's MANAGED IDENTITY in the cloud (and the developer's
    //     az-login locally) — no secret to store, leak, or rotate.
    //   * If neither is configured, no client is built and the sender becomes a safe no-op.
    public static IServiceCollection AddAcsEmailSender(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AcsEmailOptions>(configuration.GetSection("Acs"));

        // The EmailClient (which may be null when ACS is unconfigured) is built here and handed to
        // the sender, rather than registered as its own DI service — a nullable service would trip
        // the container's non-null 'class' constraint.
        services.AddSingleton<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AcsEmailOptions>>();
            var logger = sp.GetRequiredService<ILogger<AcsEmailSender>>();
            var client = BuildClient(options.Value, logger);
            return new AcsEmailSender(client, options, logger);
        });

        return services;
    }

    private static EmailClient? BuildClient(AcsEmailOptions o, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(o.ConnectionString))
        {
            logger.LogInformation("ACS email: using connection-string authentication (Key Vault fallback).");
            return new EmailClient(o.ConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(o.Endpoint))
        {
            logger.LogInformation(
                "ACS email: using Entra (managed identity) authentication against {Endpoint}.", o.Endpoint);
            return new EmailClient(new Uri(o.Endpoint), new DefaultAzureCredential());
        }

        logger.LogInformation(
            "ACS email: no Acs:Endpoint or Acs:ConnectionString configured; email sending is disabled (no-op).");
        return null;
    }
}
