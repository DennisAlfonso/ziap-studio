using ZiapStudio.Core.Localization;

namespace ZiapStudio.Services.Integration.Remote;

public interface IRemoteLocalizationClient
{
    Task<RemoteLocalizationManifest> GetManifestAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task<PublishedLocalizationFile> GetPublishedFileAsync(
        string projectId,
        RemoteLocalizationManifestFile file,
        CancellationToken cancellationToken = default);
}
