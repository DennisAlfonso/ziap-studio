using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ZiapStudio.Core.Localization;
using ZiapStudio.Services.Authentication;

namespace ZiapStudio.Services.Integration.Remote;

public sealed class HttpRemoteLocalizationClient : IRemoteLocalizationClient
{
    private const int MaximumErrorBodyLength = 512;
    private const int MaximumPublishedFileBytes = 10 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly Uri _manifestEndpoint;
    private readonly Uri _publishedFileEndpoint;
    private readonly IIdTokenProvider? _idTokenProvider;
    private readonly string? _developmentAccessToken;

    public HttpRemoteLocalizationClient(
        HttpClient httpClient,
        Uri manifestEndpoint,
        string? developmentAccessToken = null)
        : this(
            httpClient,
            manifestEndpoint,
            BuildSiblingEndpoint(manifestEndpoint, "getLocalizationPublishedFile"),
            null,
            developmentAccessToken)
    {
    }

    public HttpRemoteLocalizationClient(
        HttpClient httpClient,
        Uri manifestEndpoint,
        Uri publishedFileEndpoint,
        IIdTokenProvider? idTokenProvider,
        string? developmentAccessToken = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ValidateEndpoint(manifestEndpoint, nameof(manifestEndpoint));
        ValidateEndpoint(publishedFileEndpoint, nameof(publishedFileEndpoint));

        _httpClient = httpClient;
        _manifestEndpoint = manifestEndpoint;
        _publishedFileEndpoint = publishedFileEndpoint;
        _idTokenProvider = idTokenProvider;
        _developmentAccessToken = string.IsNullOrWhiteSpace(developmentAccessToken)
            ? null
            : developmentAccessToken.Trim();
    }

    public async Task<RemoteLocalizationManifest> GetManifestAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectId = NormalizeProjectId(projectId);
        var requestUri = new UriBuilder(_manifestEndpoint)
        {
            Query = $"projectId={Uri.EscapeDataString(normalizedProjectId)}",
        }.Uri;
        using var request = await CreateAuthorizedRequestAsync(requestUri, cancellationToken);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadErrorDetailAsync(response, cancellationToken);
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    "Accesso alla ZIAP API non configurato o non autorizzato.",
                HttpStatusCode.NotFound =>
                    "Il progetto non ha un manifest Localization remoto.",
                _ => $"ZIAP API ha risposto con stato {(int)response.StatusCode}.",
            };
            throw new RemoteLocalizationException(
                string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync(
                stream,
                RemoteLocalizationJsonContext.Default.RemoteLocalizationManifest,
                cancellationToken);
            if (manifest is null ||
                !manifest.ProjectId.Equals(normalizedProjectId, StringComparison.OrdinalIgnoreCase))
            {
                throw new RemoteLocalizationException(
                    "Il manifest remoto ricevuto non corrisponde al progetto aperto.");
            }
            return manifest;
        }
        catch (RemoteLocalizationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new RemoteLocalizationException(
                "ZIAP API ha restituito un manifest non valido.",
                exception);
        }
    }

    public async Task<PublishedLocalizationFile> GetPublishedFileAsync(
        string projectId,
        RemoteLocalizationManifestFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        var normalizedProjectId = NormalizeProjectId(projectId);
        var locale = NormalizePathSegment(file.Locale, nameof(file.Locale));
        var relativeFile = NormalizeRelativeFile(file.File);
        var versionId = file.VersionId?.Trim();
        if (string.IsNullOrWhiteSpace(versionId) ||
            versionId.Length > 200 ||
            versionId.Contains('/'))
        {
            throw new ArgumentException("Versione remota non valida.", nameof(file));
        }

        var requestUri = new UriBuilder(_publishedFileEndpoint)
        {
            Query = string.Join('&',
            [
                $"projectId={Uri.EscapeDataString(normalizedProjectId)}",
                $"locale={Uri.EscapeDataString(locale)}",
                $"file={Uri.EscapeDataString(relativeFile)}",
                $"versionId={Uri.EscapeDataString(versionId)}",
            ]),
        }.Uri;
        using var request = await CreateAuthorizedRequestAsync(requestUri, cancellationToken);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadErrorDetailAsync(response, cancellationToken);
            var message = response.StatusCode switch
            {
                HttpStatusCode.Conflict =>
                    "La versione pubblicata è cambiata. Verifica di nuovo il workspace.",
                HttpStatusCode.NotFound =>
                    "Il file non ha una versione pubblicata disponibile.",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    "La sessione myZenkai non è autorizzata a scaricare il file.",
                _ => $"ZIAP API ha risposto con stato {(int)response.StatusCode}.",
            };
            throw new RemoteLocalizationException(
                string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}");
        }

        var returnedVersion = GetHeader(response, "X-ZIAP-Version-Id");
        if (!versionId.Equals(returnedVersion, StringComparison.Ordinal))
        {
            throw new RemoteLocalizationException(
                "La ZIAP API ha restituito una versione diversa da quella richiesta.");
        }

        var contents = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (contents.Length > MaximumPublishedFileBytes)
        {
            throw new RemoteLocalizationException(
                "Il file pubblicato supera il limite supportato di 10 MB.");
        }

        return new PublishedLocalizationFile
        {
            ProjectId = normalizedProjectId,
            Locale = locale,
            File = relativeFile,
            VersionId = versionId,
            Checksum = GetHeader(response, "X-ZIAP-Checksum") ?? file.Checksum,
            ChecksumAlgorithm = GetHeader(response, "X-ZIAP-Checksum-Algorithm") ??
                file.ChecksumAlgorithm,
            Contents = contents,
        };
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(
        Uri requestUri,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ZIAP-Studio/0.1.6");
        string? idToken;
        try
        {
            idToken = _idTokenProvider is null
                ? null
                : await _idTokenProvider.GetValidIdTokenAsync(cancellationToken);
        }
        catch (AuthenticationException exception)
        {
            if (_developmentAccessToken is null)
            {
                request.Dispose();
                throw new RemoteLocalizationException(
                    "La sessione myZenkai non può essere aggiornata.",
                    exception);
            }
            idToken = null;
        }

        if (!string.IsNullOrWhiteSpace(idToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        }
        else if (_developmentAccessToken is not null)
        {
            request.Headers.Add("X-ZIAP-Studio-Token", _developmentAccessToken);
        }
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            throw new RemoteLocalizationException("ZIAP API non è raggiungibile.", exception);
        }
    }

    private static string NormalizeProjectId(string projectId)
    {
        var normalized = projectId?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 80 ||
            normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("ID progetto remoto non valido.", nameof(projectId));
        }
        return normalized;
    }

    private static string NormalizePathSegment(string value, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 32 || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Locale remoto non valido.", parameterName);
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

    private static string? GetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()?.Trim()
            : null;

    private static Uri BuildSiblingEndpoint(Uri endpoint, string functionName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var builder = new UriBuilder(endpoint)
        {
            Path = $"/{functionName}",
            Query = string.Empty,
        };
        return builder.Uri;
    }

    private static void ValidateEndpoint(Uri endpoint, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "L'endpoint remoto deve essere un URL HTTP assoluto.",
                parameterName);
        }
    }

    private static async Task<string?> ReadErrorDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return TrimDetail(error.GetString());
                }
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message))
                {
                    return TrimDetail(message.GetString());
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
        }
        return null;
    }

    private static string? TrimDetail(string? detail)
    {
        var normalized = detail?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }
        return normalized.Length <= MaximumErrorBodyLength
            ? normalized
            : normalized[..MaximumErrorBodyLength];
    }
}
