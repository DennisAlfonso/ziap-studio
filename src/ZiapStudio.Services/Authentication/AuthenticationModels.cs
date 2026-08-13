using System.Text.Json.Serialization;

namespace ZiapStudio.Services.Authentication;

public sealed record AuthenticationAccount(
    string Uid,
    string? Nickname,
    string? Email)
{
    public string DisplayName =>
        FirstNonEmpty(Nickname, Email, Uid) ?? "Account myZenkai";

    public string Detail =>
        FirstNonEmpty(Email, Uid) ?? "myZenkai Account";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public sealed record AuthenticationSession(
    AuthenticationAccount Account,
    string IdToken,
    string RefreshToken,
    DateTimeOffset IdTokenExpiresAt);

internal sealed record StoredAuthenticationCredential(
    string RefreshToken,
    string Uid,
    string? Nickname,
    string? Email);

public sealed record FirebaseTokenResult(
    string IdToken,
    string RefreshToken,
    string Uid,
    DateTimeOffset ExpiresAt);

public sealed record ZiapAuthorizationResult(
    string FirebaseCustomToken,
    AuthenticationAccount Account);

internal sealed record FirebaseCustomTokenRequest(
    string Token,
    bool ReturnSecureToken);

internal sealed record FirebaseCustomTokenResponse(
    string? IdToken,
    string? RefreshToken,
    string? ExpiresIn);

internal sealed record FirebaseRefreshTokenResponse(
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] string? ExpiresIn,
    [property: JsonPropertyName("user_id")] string? UserId);

internal sealed record ZiapOAuthTokenResponse(
    [property: JsonPropertyName("firebase_custom_token")] string? FirebaseCustomToken,
    ZiapOAuthUser? User);

internal sealed record ZiapOAuthUser(
    string? Uid,
    string? Nickname,
    string? Email);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(FirebaseCustomTokenRequest))]
[JsonSerializable(typeof(FirebaseCustomTokenResponse))]
[JsonSerializable(typeof(FirebaseRefreshTokenResponse))]
[JsonSerializable(typeof(ZiapOAuthTokenResponse))]
[JsonSerializable(typeof(StoredAuthenticationCredential))]
internal sealed partial class AuthenticationJsonContext : JsonSerializerContext
{
}
