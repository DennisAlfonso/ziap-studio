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

public sealed record ZiapApplicationSessionResult(string AppSessionToken);

public sealed record ZiapAuthorizationResult(
    string FirebaseCustomToken,
    AuthenticationAccount Account);

internal sealed record FirebaseCustomTokenRequest(
    string Token,
    bool ReturnSecureToken);

internal sealed record FirebaseCustomTokenResponse(
    string? IdToken,
    string? RefreshToken,
    string? ExpiresIn,
    [property: JsonPropertyName("localId")] string? LocalId);

internal sealed record FirebaseRefreshTokenResponse(
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] string? ExpiresIn,
    [property: JsonPropertyName("user_id")] string? UserId);

internal sealed record ZiapOAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("expires_in")] int? ExpiresIn,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("issued_at")] string? IssuedAt,
    ZiapOAuthUser? User);

internal sealed record ZiapFirebaseCustomTokenResponse(
    bool? Ok,
    string? Uid,
    string? CustomToken);

internal sealed record ZiapOAuthUser(
    string? Uid,
    string? Nickname,
    string? Email);

internal sealed record ZiapDesktopDeviceInfo(
    string Platform,
    string Model,
    string OperatingSystem,
    string OsVersion,
    string Language,
    string Timezone);

internal sealed record ZiapResolveLoginFlowRequest(
    string Application,
    string LoginMethod,
    string Platform,
    ZiapDesktopDeviceInfo DeviceInfo);

internal sealed record ZiapResolveLoginFlowCallableRequest(ZiapResolveLoginFlowRequest Data);

internal sealed record ZiapResolvedLoginUser(string? Uid);

internal sealed record ZiapLoginFlowStateResult(
    bool? Ok,
    string? NextStep,
    string? LoginAttemptId,
    ZiapResolvedLoginUser? User);

internal sealed record ZiapResolveLoginFlowCallableResponse(ZiapLoginFlowStateResult? Result);

internal sealed record ZiapConfirmLoginLegalStateRequest(string LoginAttemptId);

internal sealed record ZiapConfirmLoginLegalStateCallableRequest(ZiapConfirmLoginLegalStateRequest Data);

internal sealed record ZiapConfirmLoginLegalStateResult(bool? Success);

internal sealed record ZiapConfirmLoginLegalStateCallableResponse(
    ZiapConfirmLoginLegalStateResult? Result);

internal sealed record ZiapFinalizeLoginSessionRequest(
    string LoginAttemptId,
    string Application,
    string LoginMethod,
    string? AppVersion,
    ZiapDesktopDeviceInfo DeviceInfo);

internal sealed record ZiapFinalizeLoginSessionCallableRequest(ZiapFinalizeLoginSessionRequest Data);

internal sealed record ZiapFinalizeLoginSessionResult(bool? Ok, string? AppSessionToken);

internal sealed record ZiapFinalizeLoginSessionCallableResponse(
    ZiapFinalizeLoginSessionResult? Result);

internal sealed record FirebaseCallableError(string? Status);

internal sealed record FirebaseCallableErrorEnvelope(FirebaseCallableError? Error);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(FirebaseCustomTokenRequest))]
[JsonSerializable(typeof(FirebaseCustomTokenResponse))]
[JsonSerializable(typeof(FirebaseRefreshTokenResponse))]
[JsonSerializable(typeof(ZiapOAuthTokenResponse))]
[JsonSerializable(typeof(ZiapFirebaseCustomTokenResponse))]
[JsonSerializable(typeof(ZiapResolveLoginFlowCallableRequest))]
[JsonSerializable(typeof(ZiapResolveLoginFlowCallableResponse))]
[JsonSerializable(typeof(ZiapConfirmLoginLegalStateCallableRequest))]
[JsonSerializable(typeof(ZiapConfirmLoginLegalStateCallableResponse))]
[JsonSerializable(typeof(ZiapFinalizeLoginSessionCallableRequest))]
[JsonSerializable(typeof(ZiapFinalizeLoginSessionCallableResponse))]
[JsonSerializable(typeof(FirebaseCallableErrorEnvelope))]
[JsonSerializable(typeof(StoredAuthenticationCredential))]
internal sealed partial class AuthenticationJsonContext : JsonSerializerContext
{
}
