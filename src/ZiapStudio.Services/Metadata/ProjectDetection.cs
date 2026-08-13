using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Metadata;

public sealed record ProjectDetection(
    string ProjectPath,
    string DetectedProjectType,
    bool HasZiapMetadata,
    bool HasPackage)
{
    public bool IsRecognized =>
        HasZiapMetadata || HasPackage || DetectedProjectType != KnownProjectTypes.Unknown;
}
