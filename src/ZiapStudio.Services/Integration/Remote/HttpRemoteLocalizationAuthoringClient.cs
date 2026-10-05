using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ZiapStudio.Core.Localization;
using ZiapStudio.Services.Authentication;

namespace ZiapStudio.Services.Integration.Remote;

/// <summary>
/// Authenticated authoring client. Unlike read-only synchronization it never uses
/// the development-token fallback: mutation endpoints require a Firebase ID token.
/// </summary>
public sealed class HttpRemoteLocalizationAuthoringClient : IRemoteLocalizationAuthoringClient
{
    private const int MaximumResponseBytes = 10 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly IIdTokenProvider _idTokenProvider;
    private readonly Uri _getEndpoint;
    private readonly Uri _claimEndpoint;
    private readonly Uri _renewEndpoint;
    private readonly Uri _releaseEndpoint;
    private readonly Uri _patchEndpoint;
    private readonly Uri _appendEndpoint;

    public HttpRemoteLocalizationAuthoringClient(
        HttpClient httpClient,
        Uri getEndpoint,
        Uri claimEndpoint,
        Uri renewEndpoint,
        Uri releaseEndpoint,
        Uri patchEndpoint,
        Uri appendEndpoint,
        IIdTokenProvider idTokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _idTokenProvider = idTokenProvider ?? throw new ArgumentNullException(nameof(idTokenProvider));
        _getEndpoint = ValidateEndpoint(getEndpoint, nameof(getEndpoint));
        _claimEndpoint = ValidateEndpoint(claimEndpoint, nameof(claimEndpoint));
        _renewEndpoint = ValidateEndpoint(renewEndpoint, nameof(renewEndpoint));
        _releaseEndpoint = ValidateEndpoint(releaseEndpoint, nameof(releaseEndpoint));
        _patchEndpoint = ValidateEndpoint(patchEndpoint, nameof(patchEndpoint));
        _appendEndpoint = ValidateEndpoint(appendEndpoint, nameof(appendEndpoint));
    }

    public Task<LocalizationAuthoringSnapshot> GetAuthoringFileAsync(
        string projectId, string locale, string sourceFile, CancellationToken cancellationToken = default) =>
        SendSnapshotAsync(_getEndpoint, HttpMethod.Get, projectId, locale, sourceFile, null, cancellationToken);

    public async Task<LocalizationLockInfo> ClaimLockAsync(
        string projectId, string locale, string sourceFile, CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync<LockEnvelope>(
            _claimEndpoint, projectId, locale, sourceFile, new { }, cancellationToken);
        return ToLockInfo(response.Lock);
    }

    public async Task<LocalizationLockInfo> RenewLockAsync(
        string projectId, string locale, string sourceFile, string? acquiredAt,
        CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync<LockEnvelope>(
            _renewEndpoint, projectId, locale, sourceFile, new { acquiredAt }, cancellationToken);
        return ToLockInfo(response.Lock);
    }

    public async Task ReleaseLockAsync(
        string projectId, string locale, string sourceFile, string? acquiredAt,
        CancellationToken cancellationToken = default)
    {
        await SendJsonAsync<ReleaseResponse>(
            _releaseEndpoint, projectId, locale, sourceFile, new { acquiredAt }, cancellationToken);
    }

    public async Task<LocalizationAuthoringSnapshot> PatchStagingAsync(
        string projectId,
        string locale,
        string sourceFile,
        string? basedOnVersionId,
        string? expectedStagingChecksum,
        IReadOnlyList<LocalizationScalarPatch> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var payload = new
        {
            basedOnVersionId,
            expectedStagingChecksum,
            changes = changes.Select(change => new
            {
                path = change.Path.Select(segment => segment.PropertyName is { } name
                    ? new { kind = "property", name = (string?)name, index = (int?)null }
                    : new { kind = "index", name = (string?)null, index = segment.ArrayIndex }).ToArray(),
                expectedOldValue = change.ExpectedOldValue,
                value = change.Value,
            }).ToArray(),
        };
        return await SendSnapshotAsync(
            _patchEndpoint, HttpMethod.Post, projectId, locale, sourceFile, payload, cancellationToken);
    }

    public async Task<LocalizationAppendResult> AppendStagingEntryAsync(
        LocalizationAppendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedArrayLength < 0 || string.IsNullOrWhiteSpace(request.OperationId) ||
            string.IsNullOrWhiteSpace(request.BasedOnVersionId))
        {
            throw new ArgumentException("Richiesta di append Localization non valida.", nameof(request));
        }

        var normalizedTemplate = request.Template.Trim().ToLowerInvariant();
        if (normalizedTemplate is not ("dialogue" or "narration"))
        {
            throw new ArgumentException("Template di append Localization non supportato.", nameof(request));
        }

        object values = normalizedTemplate == "dialogue"
            ? new { name = request.Speaker, text = request.Text }
            : new { text = request.Text };
        var payload = new
        {
            operationId = request.OperationId,
            basedOnVersionId = request.BasedOnVersionId,
            expectedStagingChecksum = request.ExpectedStagingChecksum,
            expectedArrayLength = request.ExpectedArrayLength,
            branchPath = SerializePath(request.BranchPath),
            template = normalizedTemplate,
            values,
        };
        var response = await SendAsync<AppendResponse>(
            _appendEndpoint, HttpMethod.Post, request.ProjectId, request.Locale, request.SourceFile,
            payload, cancellationToken);
        if (response.AssignedIndex is null || response.EntryPath is null || response.EntryPath.Length == 0)
        {
            throw new RemoteLocalizationAuthoringException(
                RemoteLocalizationAuthoringFailure.Unknown,
                "La ZIAP API non ha restituito il path autorevole dell'append.");
        }

        return new LocalizationAppendResult
        {
            OperationId = response.OperationId ?? request.OperationId,
            AssignedIndex = response.AssignedIndex.Value,
            EntryPath = DeserializePath(response.EntryPath),
            Snapshot = ToSnapshot(response, request.ProjectId, request.Locale, request.SourceFile),
            IsIdempotentReplay = response.IsIdempotentReplay,
        };
    }

    private async Task<LocalizationAuthoringSnapshot> SendSnapshotAsync(
        Uri endpoint,
        HttpMethod method,
        string projectId,
        string locale,
        string sourceFile,
        object? payload,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<AuthoringSnapshotResponse>(
            endpoint, method, projectId, locale, sourceFile, payload, cancellationToken);
        return ToSnapshot(response, projectId, locale, sourceFile);
    }

    private static LocalizationAuthoringSnapshot ToSnapshot(
        AuthoringSnapshotResponse response, string projectId, string locale, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new RemoteLocalizationAuthoringException(
                RemoteLocalizationAuthoringFailure.Unknown,
                "La ZIAP API non ha restituito il contenuto Localization canonico.");
        }
        return new LocalizationAuthoringSnapshot
        {
            ProjectId = response.ProjectId ?? projectId,
            Locale = response.Locale ?? locale,
            SourceFile = response.File ?? sourceFile,
            Content = response.Content,
            CurrentVersionId = response.CurrentVersionId,
            CurrentChecksum = response.CurrentChecksum,
            StagingBasedOnVersionId = response.StagingBasedOnVersionId,
            StagingChecksum = response.StagingChecksum,
            Source = string.Equals(response.Source, "staging", StringComparison.OrdinalIgnoreCase)
                ? LocalizationAuthoringSource.Staging
                : LocalizationAuthoringSource.Published,
            Lock = ToLockInfo(response.Lock),
        };
    }

    private static object[] SerializePath(IReadOnlyList<LocalizationPathSegment> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Select(segment => segment.PropertyName is { } name
            ? (object)new { kind = "property", name, index = (int?)null }
            : new { kind = "index", name = (string?)null, index = segment.ArrayIndex }).ToArray();
    }

    private static IReadOnlyList<LocalizationPathSegment> DeserializePath(
        IReadOnlyList<PathSegmentResponse> path) => path.Select(segment =>
        string.Equals(segment.Kind, "property", StringComparison.OrdinalIgnoreCase)
            ? LocalizationPathSegment.Property(segment.Name ?? throw new JsonException("Segmento property senza name."))
            : string.Equals(segment.Kind, "index", StringComparison.OrdinalIgnoreCase) && segment.Index is >= 0
                ? LocalizationPathSegment.Index(segment.Index.Value)
                : throw new JsonException("Segmento path autorevole non valido.")).ToArray();

    private async Task<T> SendJsonAsync<T>(
        Uri endpoint, string projectId, string locale, string sourceFile, object? payload,
        CancellationToken cancellationToken) => await SendAsync<T>(
            endpoint, HttpMethod.Post, projectId, locale, sourceFile, payload, cancellationToken);

    private async Task<T> SendAsync<T>(
        Uri endpoint,
        HttpMethod method,
        string projectId,
        string locale,
        string sourceFile,
        object? payload,
        CancellationToken cancellationToken)
    {
        var normalizedProjectId = NormalizeProjectId(projectId);
        var normalizedLocale = NormalizeLocale(locale);
        var normalizedFile = NormalizeRelativeFile(sourceFile);
        var uri = new UriBuilder(endpoint)
        {
            Query = method == HttpMethod.Get
                ? $"projectId={Uri.EscapeDataString(normalizedProjectId)}&locale={Uri.EscapeDataString(normalizedLocale)}&file={Uri.EscapeDataString(normalizedFile)}"
                : string.Empty,
        }.Uri;
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ZIAP-Studio/0.3B");
        try
        {
            var idToken = await _idTokenProvider.GetValidIdTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(idToken))
            {
                throw new AuthenticationException("Una sessione myZenkai autenticata è obbligatoria per Story Authoring.");
            }
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        }
        catch (AuthenticationException exception)
        {
            throw new RemoteLocalizationAuthoringException(
                RemoteLocalizationAuthoringFailure.Unauthorized,
                "Autenticazione myZenkai richiesta per modificare lo staging.", exception);
        }
        if (method != HttpMethod.Get)
        {
            var body = new { projectId = normalizedProjectId, locale = normalizedLocale, file = normalizedFile, payload };
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateErrorAsync(response, cancellationToken);
            }
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            {
                throw new RemoteLocalizationAuthoringException(
                    RemoteLocalizationAuthoringFailure.Unknown, "Risposta authoring troppo grande.");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken) ??
                throw new RemoteLocalizationAuthoringException(
                    RemoteLocalizationAuthoringFailure.Unknown, "Risposta authoring JSON non valida.");
        }
        catch (RemoteLocalizationAuthoringException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new RemoteLocalizationAuthoringException(
                RemoteLocalizationAuthoringFailure.Network, "ZIAP API authoring non è raggiungibile.", exception);
        }
    }

    private static async Task<RemoteLocalizationAuthoringException> CreateErrorAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? code = null;
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                code = error.TryGetProperty("code", out var errorCode) ? errorCode.GetString() : null;
                message = error.TryGetProperty("message", out var errorMessage) ? errorMessage.GetString() : null;
            }
        }
        catch (JsonException)
        {
        }
        var failure = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => RemoteLocalizationAuthoringFailure.Unauthorized,
            HttpStatusCode.Conflict when string.Equals(code, "locked-by-other", StringComparison.Ordinal) ||
                string.Equals(code, "lock-lost", StringComparison.Ordinal) =>
                RemoteLocalizationAuthoringFailure.LockedByOther,
            HttpStatusCode.Conflict => RemoteLocalizationAuthoringFailure.Conflict,
            HttpStatusCode.BadRequest => RemoteLocalizationAuthoringFailure.InvalidRequest,
            _ => RemoteLocalizationAuthoringFailure.Unknown,
        };
        return new RemoteLocalizationAuthoringException(
            failure, message ?? $"ZIAP API authoring ha risposto con stato {(int)response.StatusCode}.");
    }

    private static LocalizationLockInfo ToLockInfo(LockResponse? response) => new()
    {
        Owner = response?.Owner,
        AcquiredAt = response?.AcquiredAt,
        ExpiresAt = response?.ExpiresAt,
        IsOwnedByCurrentUser = response?.IsOwnedByCurrentUser == true,
    };

    private static Uri ValidateEndpoint(Uri endpoint, string name)
    {
        if (endpoint is null || !endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Endpoint authoring HTTP non valido.", name);
        }
        return endpoint;
    }

    private static string NormalizeProjectId(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 80 || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("ID progetto remoto non valido.", nameof(value));
        }
        return normalized;
    }

    private static string NormalizeLocale(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 32 || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Locale remoto non valido.", nameof(value));
        }
        return normalized;
    }

    private static string NormalizeRelativeFile(string value)
    {
        var normalized = value?.Replace('\\', '/').Trim().TrimStart('/') ?? string.Empty;
        if (!normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("File remoto non valido.", nameof(value));
        }
        return normalized;
    }

    private record AuthoringSnapshotResponse
    {
        public string? ProjectId { get; init; }
        public string? Locale { get; init; }
        public string? File { get; init; }
        public string? Content { get; init; }
        public string? CurrentVersionId { get; init; }
        public string? CurrentChecksum { get; init; }
        public string? StagingBasedOnVersionId { get; init; }
        public string? StagingChecksum { get; init; }
        public string? Source { get; init; }
        public LockResponse? Lock { get; init; }
    }

    private sealed record PathSegmentResponse
    {
        public string? Kind { get; init; }
        public string? Name { get; init; }
        public int? Index { get; init; }
    }

    private sealed record AppendResponse : AuthoringSnapshotResponse
    {
        public string? OperationId { get; init; }
        public int? AssignedIndex { get; init; }
        public PathSegmentResponse[]? EntryPath { get; init; }
        public bool IsIdempotentReplay { get; init; }
    }

    private sealed record LockResponse
    {
        public string? Owner { get; init; }
        public string? AcquiredAt { get; init; }
        public string? ExpiresAt { get; init; }
        public bool IsOwnedByCurrentUser { get; init; }
    }

    private sealed record LockEnvelope
    {
        public LockResponse? Lock { get; init; }
    }

    private sealed record ReleaseResponse(bool Released);
}
