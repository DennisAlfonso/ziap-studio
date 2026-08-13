using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Metadata;

public sealed class ProjectDetector
{
    private readonly FileSystemService _fileSystem;

    public ProjectDetector(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public ProjectDetection Detect(string projectPath)
    {
        var hasZiapMetadata = _fileSystem.FileExists(
            Path.Combine(projectPath, ZiapProjectMetadataSource.MetadataRelativePath));
        var hasPackage = _fileSystem.FileExists(Path.Combine(projectPath, "package.json"));

        return new ProjectDetection(
            projectPath,
            DetectProjectType(projectPath, hasPackage),
            hasZiapMetadata,
            hasPackage);
    }

    private string DetectProjectType(string projectPath, bool hasPackage)
    {
        if (_fileSystem.FileExists(Path.Combine(projectPath, "js", "rmmz_core.js")) ||
            _fileSystem.EnumerateFiles(projectPath, "*.rmmzproject").Any())
        {
            return KnownProjectTypes.RpgMakerMz;
        }

        if (_fileSystem.FileExists(Path.Combine(projectPath, "js", "rpg_core.js")) ||
            _fileSystem.EnumerateFiles(projectPath, "*.rpgproject").Any())
        {
            return KnownProjectTypes.RpgMakerMv;
        }

        if (hasPackage && _fileSystem.FileExists(Path.Combine(projectPath, "index.html")))
        {
            return KnownProjectTypes.Web;
        }

        return KnownProjectTypes.Unknown;
    }
}
