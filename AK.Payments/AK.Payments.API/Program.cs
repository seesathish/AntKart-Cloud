using AK.BuildingBlocks.Authentication;
using AK.BuildingBlocks.Configuration;
using AK.BuildingBlocks.HealthChecks;
using AK.BuildingBlocks.Observability;
using AK.BuildingBlocks.Logging;
using AK.BuildingBlocks.Swagger;
using AK.Payments.API.Endpoints;
using AK.Payments.API.Extensions;
using AK.BuildingBlocks.Middleware;
using AK.Payments.Application.Extensions;
using AK.Payments.Infrastructure.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// M3 Step 1 — load configuration/secrets from Azure Key Vault (when KeyVault:Uri is set),
// using this service's own Entra identity, before anything reads configuration. This is how the
// Razorpay sandbox credentials (vaulted as Razorpay--KeyId / Razorpay--KeySecret) flow into
// IConfiguration as Razorpay:KeyId / Razorpay:KeySecret and bind to RazorpaySettings — no secret
// is committed to the repo.
builder.Configuration.AddAzureKeyVaultConfiguration(builder.Configuration);

builder.AddSerilogLogging();
builder.AddOpenTelemetryObservability("AK.Payments.API");

// Non-secret startup confirmation: record WHETHER Key Vault configuration was loaded, and from
// which vault URI (a non-secret value). Secret values are never logged.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (string.IsNullOrWhiteSpace(keyVaultUri))
    Log.Information("Key Vault configuration source not configured (KeyVault:Uri absent); using local configuration only");
else
    Log.Information("Key Vault configuration loaded from {KeyVaultUri}", keyVaultUri);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEntraAuthentication(builder.Configuration);
builder.Services.AddDefaultHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AK.Payments API", Version = "v1", Description = "AntKart Payments Microservice — Razorpay integration with PostgreSQL" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

await app.ApplyMigrationsAsync();

// CorrelationIdMiddleware runs OUTERMOST so the correlation id is on every log event, including
// exceptions. It reads the X-Correlation-Id forwarded by the gateway (or generates one).
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlerMiddleware>();

app.UseSwaggerInDevelopment("AK.Payments API v1");

app.UseEntraAuth();

app.MapPaymentEndpoints();
app.MapSavedCardEndpoints();
app.MapDefaultHealthChecks();

app.Run();

public partial class Program { }
