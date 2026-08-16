using AK.BuildingBlocks.Authentication;
using AK.BuildingBlocks.Configuration;
using AK.BuildingBlocks.HealthChecks;
using AK.BuildingBlocks.Observability;
using AK.BuildingBlocks.Logging;
using AK.BuildingBlocks.Swagger;
using AK.ShoppingCart.API.Endpoints;
using AK.BuildingBlocks.Middleware;
using AK.ShoppingCart.Application.Extensions;
using AK.ShoppingCart.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// Load configuration/secrets from Azure Key Vault (when KeyVault:Uri is set), using this
// service's own Entra identity, before anything reads configuration. This is how the vaulted
// RedisSettings--ConnectionString secret flows into IConfiguration as RedisSettings:ConnectionString
// and binds to RedisSettings — no secret is committed to the repo.
builder.Configuration.AddAzureKeyVaultConfiguration(builder.Configuration);

builder.AddSerilogLogging();
builder.AddOpenTelemetryObservability("AK.ShoppingCart.API");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEntraAuthentication(builder.Configuration);
builder.Services.AddDefaultHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AK.ShoppingCart API", Version = "v1", Description = "AntKart Shopping Cart Microservice — Redis-backed cart management" });
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

// CorrelationIdMiddleware runs OUTERMOST so the correlation id is on every log event, including
// exceptions. It reads the X-Correlation-Id forwarded by the gateway (or generates one).
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlerMiddleware>();

app.UseSwaggerInDevelopment("AK.ShoppingCart API v1");

app.UseEntraAuth();

app.MapCartEndpoints();
app.MapDefaultHealthChecks();

app.Run();

public partial class Program { }
