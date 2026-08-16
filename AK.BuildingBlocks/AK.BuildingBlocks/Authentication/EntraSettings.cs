namespace AK.BuildingBlocks.Authentication;

// Microsoft Entra ID settings used to validate incoming JWT access tokens.
// Every value here is NON-SECRET (tenant id, instance, audience) and is committed in
// appsettings — validating a token needs only public metadata, never a credential.
public sealed class EntraSettings
{
    // The Entra (Microsoft identity platform) cloud instance.
    public string Instance { get; init; } = "https://login.microsoftonline.com";

    // The directory (tenant) id that issues the tokens this API trusts.
    public string TenantId { get; init; } = string.Empty;

    // The API app registration's Application ID URI (api://<name>). A token carries this as its
    // `aud` when a SEPARATE client app requests a token for this API.
    public string Audience { get; init; } = string.Empty;

    // The same app registration's client/application id (a GUID). A token carries THIS as its `aud`
    // when the client and the resource are the SAME app (the API app requesting a token for itself).
    //
    // A valid `aud` may be EITHER form, so both are accepted (see ResolveValidAudiences). Tokens
    // validate whether the caller is a separate client (aud = App ID URI) or the API itself
    // (aud = client-id GUID). Both are non-secret identifiers, committed in appsettings.
    public string ClientId { get; init; } = string.Empty;

    // Optional explicit authority override. When empty it is derived as
    // {Instance}/{TenantId}/v2.0 (the Entra v2 endpoint).
    public string Authority { get; init; } = string.Empty;

    // Entra metadata is served over HTTPS, so this stays true in every environment.
    public bool RequireHttpsMetadata { get; init; } = true;

    // The OIDC authority the JWT middleware downloads metadata + signing keys from.
    public string ResolveAuthority() =>
        string.IsNullOrWhiteSpace(Authority)
            ? $"{Instance.TrimEnd('/')}/{TenantId}/v2.0"
            : Authority;

    // The exact issuer (`iss`) an Entra v2 token carries for this tenant.
    public string ResolveIssuer() =>
        $"{Instance.TrimEnd('/')}/{TenantId}/v2.0";

    // The set of `aud` values this API accepts: the App ID URI and the client-id GUID. Empty
    // values are filtered out, so a service that configures only one still validates correctly.
    public string[] ResolveValidAudiences() =>
        new[] { Audience, ClientId }
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct()
            .ToArray();
}
