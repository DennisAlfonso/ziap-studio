using System.Security.Cryptography;
using System.Text.Json;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Editing;

namespace ZiapStudio.Services.Integration.Remote;

public sealed class PublishedLocalizationSyncService
{
    private readonly FileSystemService _fileSystem;
    private readonly IRemoteLocalizationClient _remoteClient;
    private readonly LocalizationJsonDiffService _diffService;
    private readonly PublishedLocalizationFileWriter _writer;

    public PublishedLocalizationSyncService(
        FileSystemService fileSystem,
        IRemoteLocalizationClient remoteClient,
        LocalizationJsonDiffService diffService,
        PublishedLocalizationFileWriter writer)
    {
        _fileSystem = fileSystem;
        _remoteClient = remoteClient;
        _diffService = diffService;
        _writer = writer;
    }

    public async Task<PublishedLocalizationComparison> CompareAsync(
        ZiapProject project,
        RemoteLocalizationFileStatus status,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(status);
        var remote = status.Remote ?? throw new RemoteLocalizationException(
            "Il file non ha una versione pubblicata da confrontare.");
        var destinationPath = ResolveDestinationPath(project, status.Locale, status.File);
        var published = await _remoteClient.GetPublishedFileAsync(
            project.Id,
            remote,
            cancellationToken);
        ValidatePublishedFile(remote, published);

        byte[]? localContents = null;
        string? localHash = null;
        if (_fileSystem.FileExists(destinationPath))
        {
            localContents = await _fileSystem.ReadAllBytesAsync(destinationPath, cancellationToken);
            try
            {
                using var localDocument = JsonDocument.Parse(localContents);
            }
            catch (JsonException exception)
            {
                throw new RemoteLocalizationException(
                    "Il file locale non contiene JSON valido e non può essere confrontato.",
                    exception);
            }
            localHash = Convert.ToHexString(SHA256.HashData(localContents));
        }

        ReadOnlyMemory<byte>? localMemory = null;
        if (localContents is not null)
        {
            localMemory = new ReadOnlyMemory<byte>(localContents);
        }

        return new PublishedLocalizationComparison
        {
            ProjectId = project.Id,
            Locale = status.Locale,
            File = status.File,
            VersionId = published.VersionId,
            DestinationPath = destinationPath,
            LocalFileExists = localContents is not null,
            LocalContentHash = localHash,
            Differences = _diffService.Compare(localMemory, published.Contents),
        };
    }

    public async Task<PublishedLocalizationSyncResult> SynchronizeAsync(
        ZiapProject project,
        RemoteLocalizationFileStatus status,
        PublishedLocalizationComparison comparison,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(comparison);
        var remote = status.Remote ?? throw new RemoteLocalizationException(
            "Il file non ha una versione pubblicata da scaricare.");
        if (!project.Id.Equals(comparison.ProjectId, StringComparison.OrdinalIgnoreCase) ||
            !status.Locale.Equals(comparison.Locale, StringComparison.OrdinalIgnoreCase) ||
            !status.File.Equals(comparison.File, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(remote.VersionId, comparison.VersionId, StringComparison.Ordinal))
        {
            throw new RemoteLocalizationException(
                "Il confronto non corrisponde più alla versione pubblicata selezionata.");
        }

        var destinationPath = ResolveDestinationPath(project, status.Locale, status.File);
        if (!destinationPath.Equals(comparison.DestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new RemoteLocalizationException("Percorso locale di sincronizzazione non coerente.");
        }

        var published = await _remoteClient.GetPublishedFileAsync(
            project.Id,
            remote,
            cancellationToken);
        ValidatePublishedFile(remote, published);
        var created = await _writer.WriteAsync(
            destinationPath,
            published.Contents,
            comparison.LocalContentHash,
            cancellationToken);
        return new PublishedLocalizationSyncResult(
            destinationPath,
            published.VersionId,
            created);
    }

    private static void ValidatePublishedFile(
        RemoteLocalizationManifestFile expected,
        PublishedLocalizationFile published)
    {
        try
        {
            using var document = JsonDocument.Parse(published.Contents);
        }
        catch (JsonException exception)
        {
            throw new RemoteLocalizationException(
                "La versione pubblicata non contiene JSON valido.",
                exception);
        }

        var expectedChecksum = LocalizationChecksum.Normalize(expected.Checksum);
        if (expectedChecksum is null)
        {
            throw new RemoteLocalizationException(
                "La versione pubblicata non espone un checksum verificabile.");
        }
        var algorithm = LocalizationChecksum.ResolveAlgorithm(
            expected.ChecksumAlgorithm,
            expected.Checksum);
        var actualChecksum = LocalizationChecksum.Compute(published.Contents, algorithm);
        if (!actualChecksum.Equals(expectedChecksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new RemoteLocalizationException(
                "Il download pubblicato non supera la verifica del checksum.");
        }
    }

    private static string ResolveDestinationPath(
        ZiapProject project,
        string locale,
        string file)
    {
        var localesRoot = Path.GetFullPath(Path.Combine(project.Path, "locales"));
        var candidate = Path.GetFullPath(Path.Combine(
            localesRoot,
            locale,
            file.Replace('/', Path.DirectorySeparatorChar)));
        var requiredPrefix = Path.TrimEndingDirectorySeparator(localesRoot) +
            Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase) ||
            !candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new RemoteLocalizationException(
                "Il percorso Localization remoto non è sicuro per il workspace locale.");
        }
        return candidate;
    }
}
