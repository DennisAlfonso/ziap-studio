using ZiapStudio.Core.Assets;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Assets;

public sealed class RpgMakerAssetProvider
{
    private static readonly IReadOnlyDictionary<string, RpgMakerAssetKind> AssetKinds =
        new Dictionary<string, RpgMakerAssetKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["icons"] = RpgMakerAssetKind.Icon,
            ["faces"] = RpgMakerAssetKind.Face,
            ["characters"] = RpgMakerAssetKind.Character,
            ["sv-actors"] = RpgMakerAssetKind.SideViewActor,
            ["enemies"] = RpgMakerAssetKind.EnemyBattler,
            ["images"] = RpgMakerAssetKind.Image,
        };

    private readonly FileSystemService _fileSystem;

    public RpgMakerAssetProvider(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public bool CanResolve(RpgMakerResolvedValue value) =>
        value.Kind == RpgMakerResolvedValueKind.AssetReference &&
        value.ReferenceTarget is not null &&
        AssetKinds.ContainsKey(value.ReferenceTarget);

    public AssetReference Resolve(
        ZiapProject project,
        RpgMakerDatabaseEntry entry,
        string fieldKey,
        RpgMakerResolvedValue value)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(value);

        if (value.ReferenceTarget is null ||
            !AssetKinds.TryGetValue(value.ReferenceTarget, out var assetKind))
        {
            return Invalid(
                RpgMakerAssetKind.Image,
                value.RawValue,
                "Il tipo di asset non è supportato.");
        }

        if (string.IsNullOrWhiteSpace(value.RawValue) && assetKind != RpgMakerAssetKind.Icon)
        {
            return new AssetReference
            {
                AssetKind = assetKind,
                RawValue = value.RawValue,
                Status = AssetResolutionStatus.Empty,
            };
        }

        var index = ResolveIndex(assetKind, entry, value.RawValue);
        if (assetKind == RpgMakerAssetKind.Icon && index is null)
        {
            return Invalid(assetKind, value.RawValue, "L'Icon ID non è un numero valido.");
        }

        var candidatePaths = GetCandidatePaths(assetKind, value.RawValue);
        if (candidatePaths.Count == 0)
        {
            return Invalid(assetKind, value.RawValue, "Il nome dell'asset non è valido.");
        }

        foreach (var relativePath in candidatePaths)
        {
            if (!TryResolveInsideProject(project.Path, relativePath, out var resolvedPath))
            {
                return Invalid(
                    assetKind,
                    value.RawValue,
                    "Il percorso dell'asset esce dalla cartella del progetto.");
            }

            if (_fileSystem.FileExists(resolvedPath))
            {
                return new AssetReference
                {
                    AssetKind = assetKind,
                    RawValue = value.RawValue,
                    ResolvedPath = resolvedPath,
                    RelativePath = relativePath.Replace('\\', '/'),
                    Index = index,
                    Status = AssetResolutionStatus.Resolved,
                };
            }
        }

        var missingRelativePath = candidatePaths[0];
        _ = TryResolveInsideProject(project.Path, missingRelativePath, out var missingPath);
        return new AssetReference
        {
            AssetKind = assetKind,
            RawValue = value.RawValue,
            ResolvedPath = missingPath,
            RelativePath = missingRelativePath.Replace('\\', '/'),
            Index = index,
            Status = AssetResolutionStatus.Missing,
            Diagnostic = $"Asset mancante: {missingRelativePath.Replace('\\', '/')}",
        };
    }

    private static int? ResolveIndex(
        RpgMakerAssetKind assetKind,
        RpgMakerDatabaseEntry entry,
        string rawValue) => assetKind switch
        {
            RpgMakerAssetKind.Icon => int.TryParse(rawValue, out var iconIndex)
                ? iconIndex
                : null,
            RpgMakerAssetKind.Face => ParseIndex(entry.GetValue("faceIndex").RawValue),
            RpgMakerAssetKind.Character => ParseIndex(entry.GetValue("characterIndex").RawValue),
            _ => null,
        };

    private static int ParseIndex(string rawValue) =>
        int.TryParse(rawValue, out var index) ? index : 0;

    private static IReadOnlyList<string> GetCandidatePaths(
        RpgMakerAssetKind assetKind,
        string rawValue)
    {
        if (assetKind == RpgMakerAssetKind.Icon)
        {
            return [Path.Combine("img", "system", "IconSet.png")];
        }

        if (!TryNormalizeAssetName(rawValue, out var assetName))
        {
            return [];
        }

        return assetKind switch
        {
            RpgMakerAssetKind.Face => [Path.Combine("img", "faces", assetName)],
            RpgMakerAssetKind.Character => [Path.Combine("img", "characters", assetName)],
            RpgMakerAssetKind.SideViewActor => [Path.Combine("img", "sv_actors", assetName)],
            RpgMakerAssetKind.EnemyBattler =>
            [
                Path.Combine("img", "enemies", assetName),
                Path.Combine("img", "sv_enemies", assetName),
            ],
            RpgMakerAssetKind.Image => [Path.Combine("img", "pictures", assetName)],
            _ => [],
        };
    }

    private static bool TryNormalizeAssetName(string rawValue, out string assetName)
    {
        assetName = string.Empty;
        var name = rawValue.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(name) ||
            Path.IsPathRooted(name) ||
            name.Split(Path.DirectorySeparatorChar).Any(
                segment => segment is "." or ".."))
        {
            return false;
        }

        assetName = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name}.png";
        return true;
    }

    private static bool TryResolveInsideProject(
        string projectPath,
        string relativePath,
        out string resolvedPath)
    {
        var projectRoot = Path.GetFullPath(projectPath);
        resolvedPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        var relativeToProject = Path.GetRelativePath(projectRoot, resolvedPath);
        return !Path.IsPathRooted(relativeToProject) &&
            relativeToProject != ".." &&
            !relativeToProject.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal);
    }

    private static AssetReference Invalid(
        RpgMakerAssetKind assetKind,
        string rawValue,
        string diagnostic) => new()
    {
        AssetKind = assetKind,
        RawValue = rawValue,
        Status = AssetResolutionStatus.Invalid,
        Diagnostic = diagnostic,
    };
}
