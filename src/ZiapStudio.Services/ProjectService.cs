using ZiapStudio.Core.Models;
using ZiapStudio.Services.Metadata;

namespace ZiapStudio.Services;

public sealed class ProjectService
{
    private readonly FileSystemService _fileSystem;
    private readonly ProjectDetector _projectDetector;
    private readonly ProjectMetadataResolver _metadataResolver;

    public ProjectService(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
        _projectDetector = new ProjectDetector(fileSystem);
        _metadataResolver = new ProjectMetadataResolver(
            new IProjectMetadataSource[]
            {
                new ZiapProjectMetadataSource(fileSystem),
                new RpgMakerProjectMetadataSource(fileSystem),
                new PackageProjectMetadataSource(fileSystem),
                new FolderProjectMetadataSource(fileSystem),
            });
    }

    public async Task<ZiapProject> LoadAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ProjectLoadException("Seleziona una cartella di progetto.");
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.TrimEndingDirectorySeparator(_fileSystem.GetFullPath(projectPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ProjectLoadException("Il percorso selezionato non è valido.", exception);
        }

        if (!_fileSystem.DirectoryExists(normalizedPath))
        {
            throw new ProjectLoadException("La cartella selezionata non esiste più.");
        }

        var detection = _projectDetector.Detect(normalizedPath);
        if (!detection.IsRecognized)
        {
            throw new ProjectLoadException(
                "La cartella non contiene .ziap/project.json, package.json o un progetto RPG Maker riconoscibile.");
        }

        return await _metadataResolver.ResolveAsync(detection, cancellationToken);
    }
}
