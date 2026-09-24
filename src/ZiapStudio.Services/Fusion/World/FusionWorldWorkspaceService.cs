using System.Security.Cryptography;
using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.World;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Fusion.World;

public sealed class FusionWorldWorkspaceService
{
    private static readonly string[] SlotNames = ["A1", "A2", "A3", "A4", "A5", "B", "C", "D", "E"];

    private readonly FileSystemService _fileSystem;

    public FusionWorldWorkspaceService(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<FusionWorldWorkspaceDocument> LoadAsync(
        ZiapProject project,
        DocumentDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(descriptor);

        var diagnostics = new List<FusionWorldDiagnostic>();
        var tilesetDefinitions = await LoadTilesetsAsync(project, diagnostics, cancellationToken);
        var mapNames = await LoadMapNamesAsync(project, diagnostics, cancellationToken);
        var diskAssets = await ScanAssetsAsync(project, diagnostics, cancellationToken);
        var maps = await LoadMapsAsync(project, mapNames, tilesetDefinitions, diagnostics, cancellationToken);
        var mapsByTileset = maps
            .GroupBy(map => map.TilesetId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FusionWorldMapUsage>)group
                    .OrderBy(map => map.Id)
                    .Select(map => new FusionWorldMapUsage
                    {
                        MapId = map.Id,
                        MapName = map.DisplayName,
                        SourcePath = map.SourcePath,
                    })
                    .ToArray());

        var tilesets = tilesetDefinitions.Values
            .OrderBy(tileset => tileset.Id)
            .Select(definition => new FusionWorldTileset
            {
                Id = definition.Id,
                Name = definition.Name,
                Slots = SlotNames.Select((slot, index) =>
                {
                    var assetPath = index < definition.AssetPaths.Count
                        ? definition.AssetPaths[index]
                        : string.Empty;
                    return new FusionWorldTilesetSlot
                    {
                        Slot = slot,
                        AssetPath = assetPath,
                        Exists = string.IsNullOrWhiteSpace(assetPath) || diskAssets.ContainsKey(assetPath),
                    };
                }).ToArray(),
                Maps = mapsByTileset.GetValueOrDefault(definition.Id, []),
            })
            .ToArray();
        var tilesetsById = tilesets.ToDictionary(tileset => tileset.Id);

        foreach (var map in maps.Where(map => !tilesetsById.ContainsKey(map.TilesetId)))
        {
            diagnostics.Add(new FusionWorldDiagnostic
            {
                Code = "map.tileset-missing",
                Severity = FusionWorldDiagnosticSeverity.Error,
                MapId = map.Id,
                TilesetId = map.TilesetId,
                Message = $"{map.IdText} usa il tileset {map.TilesetId}, che non esiste in Tilesets.json.",
                Details = map.SourcePath,
            });
        }

        var tilesetsByAsset = tilesets
            .SelectMany(tileset => tileset.Slots
                .Where(slot => !string.IsNullOrWhiteSpace(slot.AssetPath))
                .Select(slot => new { slot.AssetPath, Tileset = tileset }))
            .GroupBy(item => item.AssetPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FusionWorldTileset>)group
                    .Select(item => item.Tileset)
                    .DistinctBy(tileset => tileset.Id)
                    .OrderBy(tileset => tileset.Id)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var duplicatePaths = diskAssets.Values
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Sha256))
            .GroupBy(asset => asset.Sha256!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(asset => asset.AssetPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var duplicateGroup in diskAssets.Values
                     .Where(asset => !string.IsNullOrWhiteSpace(asset.Sha256))
                     .GroupBy(asset => asset.Sha256!, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var paths = duplicateGroup
                .Select(asset => asset.AssetPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            diagnostics.Add(new FusionWorldDiagnostic
            {
                Code = "asset.duplicate",
                Severity = FusionWorldDiagnosticSeverity.Warning,
                AssetPath = paths[0],
                Message = $"{paths.Length} PNG sono byte-per-byte identici.",
                Details = string.Join(" · ", paths),
            });
        }

        var allAssetPaths = diskAssets.Keys
            .Concat(tilesetsByAsset.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var assets = new List<FusionWorldAsset>(allAssetPaths.Length);
        foreach (var assetPath in allAssetPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exists = diskAssets.TryGetValue(assetPath, out var diskAsset);
            var linkedTilesets = tilesetsByAsset.GetValueOrDefault(assetPath, []);
            var linkedMaps = linkedTilesets
                .SelectMany(tileset => tileset.Maps)
                .DistinctBy(map => map.MapId)
                .OrderBy(map => map.MapId)
                .ToArray();
            var isOrphan = exists && linkedTilesets.Count == 0;
            if (!exists)
            {
                diagnostics.Add(new FusionWorldDiagnostic
                {
                    Code = "asset.missing",
                    Severity = FusionWorldDiagnosticSeverity.Error,
                    AssetPath = assetPath,
                    Message = $"Il tilesheet '{assetPath}.png' non esiste.",
                    Details = string.Join(", ", linkedTilesets.Select(tileset =>
                        $"{tileset.IdText} {tileset.Name}")),
                });
            }
            else if (isOrphan)
            {
                diagnostics.Add(new FusionWorldDiagnostic
                {
                    Code = "asset.orphan",
                    Severity = FusionWorldDiagnosticSeverity.Warning,
                    AssetPath = assetPath,
                    Message = $"Il tilesheet '{assetPath}.png' non è referenziato da Tilesets.json.",
                    Details = diskAsset!.PhysicalPath,
                });
            }

            assets.Add(new FusionWorldAsset
            {
                AssetPath = assetPath,
                PhysicalPath = diskAsset?.PhysicalPath ?? BuildAssetPath(project.Path, assetPath),
                Exists = exists,
                SizeBytes = diskAsset?.SizeBytes,
                Sha256 = diskAsset?.Sha256,
                Tilesets = linkedTilesets,
                Maps = linkedMaps,
                IsOrphan = isOrphan,
                IsDuplicate = exists && duplicatePaths.Contains(assetPath),
            });
        }

        return new FusionWorldWorkspaceDocument
        {
            Descriptor = descriptor,
            ProjectPath = project.Path,
            Tilesets = tilesets,
            Maps = maps,
            Assets = assets,
            Diagnostics = diagnostics
                .OrderByDescending(diagnostic => diagnostic.Severity)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(diagnostic => diagnostic.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(diagnostic => diagnostic.MapId)
                .ToArray(),
        };
    }

    private async Task<Dictionary<int, ParsedTileset>> LoadTilesetsAsync(
        ZiapProject project,
        ICollection<FusionWorldDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(project.Path, "data", "Tilesets.json");
        if (!_fileSystem.FileExists(path))
        {
            diagnostics.Add(new FusionWorldDiagnostic
            {
                Code = "tilesets.file-missing",
                Severity = FusionWorldDiagnosticSeverity.Error,
                Message = "data/Tilesets.json non esiste.",
                Details = path,
            });
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(
                await _fileSystem.ReadAllTextAsync(path, cancellationToken));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(new FusionWorldDiagnostic
                {
                    Code = "tilesets.invalid-shape",
                    Severity = FusionWorldDiagnosticSeverity.Error,
                    Message = "Tilesets.json deve contenere un array RPG Maker.",
                    Details = path,
                });
                return [];
            }

            var result = new Dictionary<int, ParsedTileset>();
            var fallbackId = 0;
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    fallbackId++;
                    continue;
                }
                var id = ReadInt(entry, "id") ?? fallbackId;
                fallbackId++;
                if (id <= 0)
                {
                    continue;
                }
                if (!result.TryAdd(id, new ParsedTileset(
                    id,
                    ReadString(entry, "name") ?? $"Tileset {id}",
                    ReadAssetPaths(entry, id, diagnostics))))
                {
                    diagnostics.Add(new FusionWorldDiagnostic
                    {
                        Code = "tileset.id-duplicate",
                        Severity = FusionWorldDiagnosticSeverity.Error,
                        TilesetId = id,
                        Message = $"Tilesets.json contiene due tileset con ID {id}.",
                        Details = path,
                    });
                }
            }
            return result;
        }
        catch (JsonException exception)
        {
            diagnostics.Add(new FusionWorldDiagnostic
            {
                Code = "tilesets.json-invalid",
                Severity = FusionWorldDiagnosticSeverity.Error,
                Message = "Tilesets.json non contiene JSON valido.",
                Details = exception.Message,
            });
            return [];
        }
    }

    private async Task<Dictionary<int, string>> LoadMapNamesAsync(
        ZiapProject project,
        ICollection<FusionWorldDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(project.Path, "data", "MapInfos.json");
        if (!_fileSystem.FileExists(path))
        {
            diagnostics.Add(new FusionWorldDiagnostic
            {
                Code = "maps.info-missing",
                Severity = FusionWorldDiagnosticSeverity.Warning,
                Message = "MapInfos.json non esiste: ZIAP usa i nomi tecnici delle mappe.",
                Details = path,
            });
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(
                await _fileSystem.ReadAllTextAsync(path, cancellationToken));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            return document.RootElement.EnumerateArray()
                .Where(entry => entry.ValueKind == JsonValueKind.Object)
                .Select(entry => new { Id = ReadInt(entry, "id"), Name = ReadString(entry, "name") })
                .Where(entry => entry.Id is > 0)
                .GroupBy(entry => entry.Id!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Name ?? $"Map {group.Key:000}");
        }
        catch (JsonException exception)
        {
            diagnostics.Add(new FusionWorldDiagnostic
            {
                Code = "maps.info-invalid",
                Severity = FusionWorldDiagnosticSeverity.Warning,
                Message = "MapInfos.json non contiene JSON valido: ZIAP usa i nomi tecnici delle mappe.",
                Details = exception.Message,
            });
            return [];
        }
    }

    private async Task<Dictionary<string, DiskAsset>> ScanAssetsAsync(
        ZiapProject project,
        ICollection<FusionWorldDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(project.Path, "img", "tilesets");
        if (!_fileSystem.DirectoryExists(root))
        {
            diagnostics.Add(new FusionWorldDiagnostic
            {
                Code = "assets.directory-missing",
                Severity = FusionWorldDiagnosticSeverity.Error,
                Message = "img/tilesets non esiste.",
                Details = root,
            });
            return new Dictionary<string, DiskAsset>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, DiskAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var physicalPath in _fileSystem.EnumerateFilesRecursively(root, "*.png")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assetPath = Path.ChangeExtension(
                    Path.GetRelativePath(root, physicalPath),
                    extension: null)
                .Replace('\\', '/');
            try
            {
                var hash = await CalculateHashAsync(physicalPath, cancellationToken);
                result[assetPath] = new DiskAsset(
                    assetPath,
                    physicalPath,
                    _fileSystem.GetFileLength(physicalPath),
                    hash);
            }
            catch (IOException exception)
            {
                diagnostics.Add(new FusionWorldDiagnostic
                {
                    Code = "asset.unreadable",
                    Severity = FusionWorldDiagnosticSeverity.Warning,
                    AssetPath = assetPath,
                    Message = $"Il tilesheet '{assetPath}.png' non può essere letto.",
                    Details = exception.Message,
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(new FusionWorldDiagnostic
                {
                    Code = "asset.unreadable",
                    Severity = FusionWorldDiagnosticSeverity.Warning,
                    AssetPath = assetPath,
                    Message = $"Il tilesheet '{assetPath}.png' non può essere letto.",
                    Details = exception.Message,
                });
            }
        }
        return result;
    }

    private async Task<IReadOnlyList<FusionWorldMap>> LoadMapsAsync(
        ZiapProject project,
        IReadOnlyDictionary<int, string> mapNames,
        IReadOnlyDictionary<int, ParsedTileset> tilesets,
        ICollection<FusionWorldDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var dataDirectory = Path.Combine(project.Path, "data");
        if (!_fileSystem.DirectoryExists(dataDirectory))
        {
            return [];
        }

        var result = new List<FusionWorldMap>();
        foreach (var path in _fileSystem.EnumerateFiles(dataDirectory, "Map*.json")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);
            if (!TryReadMapId(fileName, out var mapId))
            {
                continue;
            }
            try
            {
                using var document = JsonDocument.Parse(
                    await _fileSystem.ReadAllTextAsync(path, cancellationToken));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("La radice della mappa deve essere un oggetto JSON.");
                }
                var tilesetId = ReadInt(document.RootElement, "tilesetId") ?? 0;
                result.Add(new FusionWorldMap
                {
                    Id = mapId,
                    DisplayName = mapNames.GetValueOrDefault(mapId, $"Map {mapId:000}"),
                    TilesetId = tilesetId,
                    TilesetName = tilesets.TryGetValue(tilesetId, out var tileset)
                        ? tileset.Name
                        : $"Tileset {tilesetId} mancante",
                    SourcePath = path,
                });
            }
            catch (JsonException exception)
            {
                diagnostics.Add(new FusionWorldDiagnostic
                {
                    Code = "map.json-invalid",
                    Severity = FusionWorldDiagnosticSeverity.Warning,
                    MapId = mapId,
                    Message = $"Map{mapId:000}.json non contiene JSON valido.",
                    Details = exception.Message,
                });
            }
        }
        return result.OrderBy(map => map.Id).ToArray();
    }

    private static IReadOnlyList<string> ReadAssetPaths(
        JsonElement source,
        int tilesetId,
        ICollection<FusionWorldDiagnostic> diagnostics)
    {
        if (!source.TryGetProperty("tilesetNames", out var names) ||
            names.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var result = new List<string>();
        foreach (var name in names.EnumerateArray())
        {
            var rawName = name.ValueKind == JsonValueKind.String ? name.GetString() : null;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                result.Add(string.Empty);
            }
            else if (TryNormalizeAssetPath(rawName, out var normalized))
            {
                result.Add(normalized);
            }
            else
            {
                diagnostics.Add(new FusionWorldDiagnostic
                {
                    Code = "tileset.asset-path-invalid",
                    Severity = FusionWorldDiagnosticSeverity.Error,
                    TilesetId = tilesetId,
                    Message = $"Il tileset {tilesetId} contiene un percorso sheet non sicuro.",
                    Details = rawName,
                });
                result.Add(string.Empty);
            }
        }
        return result;
    }

    private static bool TryNormalizeAssetPath(string rawPath, out string normalized)
    {
        normalized = string.Empty;
        var candidate = rawPath.Trim().Replace('\\', '/');
        if (candidate.StartsWith("/", StringComparison.Ordinal) || candidate.Contains(':'))
        {
            return false;
        }
        var parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            return false;
        }
        normalized = string.Join('/', parts);
        if (normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }
        return !string.IsNullOrWhiteSpace(normalized);
    }

    private static string BuildAssetPath(string projectPath, string assetPath) => Path.Combine(
        projectPath,
        "img",
        "tilesets",
        assetPath.Replace('/', Path.DirectorySeparatorChar) + ".png");

    private static async Task<string> CalculateHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static bool TryReadMapId(string fileName, out int mapId)
    {
        mapId = 0;
        return fileName.Length == 11 &&
            fileName.StartsWith("Map", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(fileName.AsSpan(3, 3), out mapId);
    }

    private static string? ReadString(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object &&
        source.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object &&
        source.TryGetProperty(name, out var value) &&
        value.TryGetInt32(out var result)
            ? result
            : null;

    private sealed record ParsedTileset(
        int Id,
        string Name,
        IReadOnlyList<string> AssetPaths);

    private sealed record DiskAsset(
        string AssetPath,
        string PhysicalPath,
        long SizeBytes,
        string Sha256);
}
