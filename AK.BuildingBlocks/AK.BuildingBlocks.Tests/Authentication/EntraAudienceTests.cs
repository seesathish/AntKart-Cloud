using System.Security.Cryptography;
using AK.BuildingBlocks.Authentication;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AK.BuildingBlocks.Tests.Authentication;

public sealed class EntraAudienceTests
{
    private const string AppIdUri = "api://antkart-api-dev";
    private const string ClientId = "ea22550b-a0ae-4847-a2e2-2524752d9522";

    private static EntraSettings Settings() => new() { Audience = AppIdUri, ClientId = ClientId };

    // ── ResolveValidAudiences ───────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveValidAudiences_IncludesBoth_AppIdUri_And_ClientId()
    {
        Settings().ResolveValidAudiences().Should().BeEquivalentTo(new[] { AppIdUri, ClientId });
    }

    [Fact]
    public void ResolveValidAudiences_WithOnlyAudience_ReturnsThatOne()
    {
        new EntraSettings { Audience = AppIdUri }.ResolveValidAudiences()
            .Should().ContainSingle().Which.Should().Be(AppIdUri);
    }

    [Fact]
    public void ResolveValidAudiences_WithOnlyClientId_ReturnsThatOne()
    {
        new EntraSettings { ClientId = ClientId }.ResolveValidAudiences()
            .Should().ContainSingle().Which.Should().Be(ClientId);
    }

    // ── Token audience acceptance (real JWT stack, focused on the audience check) ────────────

    private static readonly SymmetricSecurityKey TestKey =
        new(RandomNumberGenerator.GetBytes(32));

    private static string CreateToken(string audience)
    {
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Audience = audience,
            Issuer = "https://test-issuer",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(TestKey, SecurityAlgorithms.HmacSha256)
        });
    }

    private static TokenValidationParameters AudienceValidationParameters() => new()
    {
        ValidateAudience = true,
        ValidAudiences = Settings().ResolveValidAudiences(),
        ValidateIssuer = false,
        ValidateLifetime = false,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = TestKey
    };

    [Fact]
    public async Task Token_WithAudience_ClientIdGuid_IsAccepted()
    {
        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(CreateToken(ClientId), AudienceValidationParameters());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Token_WithAudience_AppIdUri_IsAccepted()
    {
        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(CreateToken(AppIdUri), AudienceValidationParameters());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Token_WithAudience_ForAnotherApi_IsRejected()
    {
        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(CreateToken("api://some-other-api"), AudienceValidationParameters());

        result.IsValid.Should().BeFalse();
    }
}
