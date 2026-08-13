using System.Text;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Integration.Remote;

public sealed class RemoteLocalizationService
{
    private readonly FileSystemService _fileSystem;
    private readonly IRemoteLocalizationClient _remoteClient;

    public RemoteLocalizationService(
        FileSystemService fileSystem,
        IRemoteLocalizationClient remoteClient)
    {
        _fileSystem = fileSystem;
        _remoteClient = remoteClient;
    }

    public async Task<RemoteLocalizationWorkspaceStatus> GetStatusAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        RemoteLocalizationManifest manifest;
        Dictionary<string, LocalLocalizationFile> localFiles;
        try
        {
            manifest = await _remoteClient.GetManifestAsync(project.Id, cancellationToken);
            localFiles = DiscoverLocalFiles(project.Path);
        }
        catch (RemoteLocalizationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new RemoteLocalizationException(
                "Impossibile leggere lo stato Localization del workspace.",
                exception);
        }

        var remoteFiles = new Dictionary<string, RemoteLocalizationManifestFile>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var remote in manifest.Files.Select(NormalizeRemoteFile))
        {
            if (!remoteFiles.TryAdd(CreateKey(remote), remote))
            {
                throw new RemoteLocalizationException(
                    $"Il manifest remoto contiene più record per {remote.Locale}/{remote.File}.");
            }
        }

        var keys = localFiles.Keys
            .Concat(remoteFiles.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var statuses = new List<RemoteLocalizationFileStatus>(keys.Length);

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            localFiles.TryGetValue(key, out var local);
            remoteFiles.TryGetValue(key, out var remote);
            statuses.Add(await CompareAsync(local, remote, cancellationToken));
        }

        return new RemoteLocalizationWorkspaceStatus
        {
            ProjectId = project.Id,
            Generation = manifest.Generation,
            GeneratedAt = manifest.GeneratedAt,
            Files = statuses,
        };
    }

    private Dictionary<string, LocalLocalizationFile> DiscoverLocalFiles(string projectPath)
    {
        var localesRoot = Path.Combine(projectPath, "locales");
        var result = new Dictionary<string, LocalLocalizationFile>(StringComparer.OrdinalIgnoreCase);
        if (!_fileSystem.DirectoryExists(localesRoot))
        {
            return result;
        }

        foreach (var path in _fileSystem.EnumerateFilesRecursively(localesRoot, "*.json"))
        {
            var relativePath = Path.GetRelativePath(localesRoot, path);
            var segments = relativePath
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                continue;
            }

            var locale = segments[0];
            var file = string.Join('/', segments.Skip(1));
            var local = new LocalLocalizationFile(locale, file, path);
            result[CreateKey(locale, file)] = local;
        }

        return result;
    }

    private async Task<RemoteLocalizationFileStatus> CompareAsync(
        LocalLocalizationFile? local,
        RemoteLocalizationManifestFile? remote,
        CancellationToken cancellationToken)
    {
        var locale = local?.Locale ?? remote!.Locale;
        var file = local?.File ?? remote!.File;
        if (local is null)
        {
            return new RemoteLocalizationFileStatus
            {
                Locale = locale,
                File = file,
                Alignment = RemoteLocalizationAlignment.MissingLocal,
                Remote = remote,
            };
        }

        var algorithm = remote is null
            ? LocalizationChecksum.Sha256
            : LocalizationChecksum.ResolveAlgorithm(remote.ChecksumAlgorithm, remote.Checksum);
        string localChecksum;
        try
        {
            var contents = await _fileSystem.ReadAllBytesAsync(local.Path, cancellationToken);
            localChecksum = LocalizationChecksum.Compute(contents, algorithm);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return new RemoteLocalizationFileStatus
            {
                Locale = locale,
                File = file,
                Alignment = RemoteLocalizationAlignment.Error,
                LocalPath = local.Path,
                Remote = remote,
                Diagnostic = "Impossibile calcolare il checksum del file locale.",
            };
        }

        if (remote is null)
        {
            return new RemoteLocalizationFileStatus
            {
                Locale = locale,
                File = file,
                Alignment = RemoteLocalizationAlignment.MissingRemote,
                LocalPath = local.Path,
                LocalChecksum = localChecksum,
            };
        }

        var remoteChecksum = LocalizationChecksum.Normalize(remote.Checksum);
        return new RemoteLocalizationFileStatus
        {
            Locale = locale,
            File = file,
            Alignment = remoteChecksum is null
                ? RemoteLocalizationAlignment.Unknown
                : localChecksum.Equals(remoteChecksum, StringComparison.OrdinalIgnoreCase)
                    ? RemoteLocalizationAlignment.Aligned
                    : RemoteLocalizationAlignment.Different,
            LocalPath = local.Path,
            LocalChecksum = localChecksum,
            Remote = remote,
            Diagnostic = remoteChecksum is null
                ? "La versione remota non espone un checksum confrontabile."
                : null,
        };
    }

    private static RemoteLocalizationManifestFile NormalizeRemoteFile(
        RemoteLocalizationManifestFile remote)
    {
        var locale = remote.Locale.Trim();
        var file = remote.File.Replace('\\', '/').Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(locale) ||
            string.IsNullOrWhiteSpace(file) ||
            file.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new RemoteLocalizationException(
                "Il manifest remoto contiene un percorso Localization non valido.");
        }

        return remote with { Locale = locale, File = file };
    }

    private static string CreateKey(RemoteLocalizationManifestFile file) =>
        CreateKey(file.Locale, file.File);

    private static string CreateKey(string locale, string file) => $"{locale}/{file}";

    private sealed record LocalLocalizationFile(string Locale, string File, string Path);
}
