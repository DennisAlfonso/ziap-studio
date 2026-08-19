using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Audio;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Integrations;

namespace ZiapStudio.Services.Fusion.Audio;

public sealed class FusionAudioCatalogService
{
    public const string RelativeCatalogPath = "data/fusion/audio.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static readonly string[] AudioExtensions = [".ogg", ".m4a", ".wav", ".mp3"];
    private readonly FileSystemService _fileSystem;
    private readonly RpgMakerPluginRegistryService _pluginRegistry;
    private readonly AtomicJsonFileWriter _atomicWriter;

    public FusionAudioCatalogService(
        FileSystemService fileSystem,
        RpgMakerPluginRegistryService pluginRegistry,
        AtomicJsonFileWriter atomicWriter)
    {
        _fileSystem = fileSystem;
        _pluginRegistry = pluginRegistry;
        _atomicWriter = atomicWriter;
    }

    public async Task<FusionAudioDocument> LoadAsync(
        ZiapProject project,
        DocumentDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(descriptor);
        var sourcePath = Path.Combine(project.Path, "data", "fusion", "audio.json");
        var availableFiles = DiscoverAudioFiles(project);
        var pluginIsActive = await _pluginRegistry.IsActiveAsync(
            project,
            FusionAudioIntegrationProvider.PluginName,
            cancellationToken);
        if (!_fileSystem.FileExists(sourcePath))
        {
            var emptyCatalog = new FusionAudioCatalog();
            return new FusionAudioDocument
            {
                Descriptor = descriptor,
                SourcePath = sourcePath,
                Catalog = emptyCatalog,
                AvailableFiles = availableFiles,
                PluginIsActive = pluginIsActive,
                Diagnostics =
                [
                    new FusionAudioDiagnostic
                    {
                        Code = "catalog.missing",
                        Severity = FusionAudioDiagnosticSeverity.Warning,
                        Message = "data/fusion/audio.json non esiste; il runtime usa il catalogo interno.",
                    },
                ],
            };
        }

        var bytes = await _fileSystem.ReadAllBytesAsync(sourcePath, cancellationToken);
        var catalog = JsonSerializer.Deserialize<FusionAudioCatalog>(bytes, JsonOptions)
            ?? throw new JsonException("data/fusion/audio.json è vuoto.");
        var (assets, diagnostics) = await AnalyzeAsync(project, catalog, cancellationToken);
        return new FusionAudioDocument
        {
            Descriptor = descriptor,
            SourcePath = sourcePath,
            Catalog = catalog,
            Assets = assets,
            AvailableFiles = availableFiles,
            Diagnostics = diagnostics,
            PluginIsActive = pluginIsActive,
            SourceSnapshot = new DocumentSourceSnapshot
            {
                SourcePath = sourcePath,
                LoadedAtUtc = DateTimeOffset.UtcNow,
                LastWriteTimeUtc = _fileSystem.GetLastWriteTimeUtc(sourcePath),
                Length = bytes.LongLength,
                ContentHash = Convert.ToHexString(SHA256.HashData(bytes)),
            },
        };
    }

    public IReadOnlyList<FusionAudioFileOption> DiscoverAudioFiles(ZiapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var audioRoot = Path.Combine(project.Path, "audio", "se");
        if (!_fileSystem.DirectoryExists(audioRoot))
        {
            return [];
        }

        var filesByCatalogPath = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var file in _fileSystem.EnumerateFilesRecursively(audioRoot, "*"))
        {
            var extension = Path.GetExtension(file);
            if (!AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(audioRoot, file).Replace('\\', '/');
            var catalogPath = Path.ChangeExtension(relativePath, null)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(catalogPath))
            {
                continue;
            }

            if (!filesByCatalogPath.TryGetValue(catalogPath, out var candidates))
            {
                candidates = [];
                filesByCatalogPath[catalogPath] = candidates;
            }
            candidates.Add(Path.GetFullPath(file));
        }

        return filesByCatalogPath
            .Select(pair =>
            {
                var orderedCandidates = pair.Value
                    .OrderBy(path => GetAudioExtensionPriority(Path.GetExtension(path)))
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new FusionAudioFileOption
                {
                    CatalogPath = pair.Key,
                    ResolvedPath = orderedCandidates[0],
                    Formats = orderedCandidates
                        .Select(path => Path.GetExtension(path).TrimStart('.').ToUpperInvariant())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                };
            })
            .OrderBy(option => option.CatalogPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<(IReadOnlyList<FusionAudioResolvedAsset> Assets,
        IReadOnlyList<FusionAudioDiagnostic> Diagnostics)> AnalyzeAsync(
        ZiapProject project,
        FusionAudioCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        var assets = new List<FusionAudioResolvedAsset>();
        var diagnostics = new List<FusionAudioDiagnostic>();
        if (catalog.SchemaVersion != 1)
        {
            diagnostics.Add(Error(
                "catalog.schema",
                $"schemaVersion {catalog.SchemaVersion} non supportata; attesa 1."));
        }
        if (!IsUnitValue(catalog.MasterVolume))
        {
            diagnostics.Add(Error("catalog.master-volume", "masterVolume deve essere compreso tra 0 e 1."));
        }

        foreach (var (categoryId, category) in catalog.Categories)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                diagnostics.Add(Error("category.id", "Una categoria non ha un identificatore."));
            }
            if (!IsUnitValue(category.Volume))
            {
                diagnostics.Add(Error(
                    "category.volume",
                    $"La categoria '{categoryId}' ha volume {category.Volume}; sono ammessi valori 0..1."));
            }
        }

        var systemSounds = await LoadSystemSoundsAsync(project, cancellationToken);
        foreach (var duplicate in catalog.Sounds.Keys
            .GroupBy(eventId => eventId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add(Error(
                "sound.id-duplicate",
                $"L'identificatore '{duplicate.Key}' compare più volte.",
                duplicate.Key));
        }
        foreach (var (eventId, entry) in catalog.Sounds)
        {
            ValidateEntry(eventId, entry, diagnostics);
            var sourceKind = entry.Source.Kind?.Trim() ?? string.Empty;
            if (sourceKind.Equals(FusionAudioSourceKinds.SystemSound, StringComparison.OrdinalIgnoreCase))
            {
                if (!FusionAudioSystemSounds.IsKnown(entry.Source.Slot))
                {
                    diagnostics.Add(Error(
                        "sound.system-slot",
                        $"Slot System Sound sconosciuto: '{entry.Source.Slot}'.",
                        eventId));
                    continue;
                }

                if (!systemSounds.TryGetValue(entry.Source.Slot!, out var systemAsset) ||
                    string.IsNullOrWhiteSpace(systemAsset))
                {
                    diagnostics.Add(Error(
                        "sound.system-empty",
                        $"System.json non configura lo slot '{entry.Source.Slot}'.",
                        eventId));
                    continue;
                }

                assets.Add(ResolveAsset(project, eventId, systemAsset, sourceKind, entry.Source.Slot));
                continue;
            }

            foreach (var file in entry.Source.Files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                assets.Add(ResolveAsset(project, eventId, file, FusionAudioSourceKinds.Files, null));
            }
        }

        foreach (var asset in assets.Where(asset => !asset.Exists))
        {
            diagnostics.Add(Error(
                "sound.asset-missing",
                $"Asset non trovato: {asset.RelativePath}.",
                asset.EventId));
        }

        return (assets, diagnostics);
    }

    public async Task<FusionAudioResolvedAsset?> ResolvePreviewAsync(
        ZiapProject project,
        string eventId,
        FusionAudioEntry entry,
        CancellationToken cancellationToken = default)
    {
        var sourceKind = entry.Source.Kind?.Trim() ?? string.Empty;
        if (sourceKind.Equals(FusionAudioSourceKinds.SystemSound, StringComparison.OrdinalIgnoreCase))
        {
            var systemSounds = await LoadSystemSoundsAsync(project, cancellationToken);
            return entry.Source.Slot is not null &&
                systemSounds.TryGetValue(entry.Source.Slot, out var name) &&
                !string.IsNullOrWhiteSpace(name)
                    ? ResolveAsset(project, eventId, name, sourceKind, entry.Source.Slot)
                    : null;
        }

        var file = entry.Source.Files.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return file is null
            ? null
            : ResolveAsset(project, eventId, file, FusionAudioSourceKinds.Files, null);
    }

    public async Task<DocumentSourceSnapshot> SaveAsync(
        ZiapProject project,
        FusionAudioCatalog catalog,
        DocumentSourceSnapshot? expectedSnapshot,
        CancellationToken cancellationToken = default)
    {
        var (_, diagnostics) = await AnalyzeAsync(project, catalog, cancellationToken);
        var errors = diagnostics.Where(diagnostic =>
            diagnostic.Severity == FusionAudioDiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                $"Il catalogo contiene {errors.Length} errori e non può essere salvato.");
        }

        var destinationPath = Path.Combine(project.Path, "data", "fusion", "audio.json");
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(catalog, JsonOptions) + Environment.NewLine);
        _fileSystem.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (expectedSnapshot is not null)
        {
            await _atomicWriter.WriteAsync(
                destinationPath,
                bytes,
                expectedSnapshot.ContentHash,
                cancellationToken);
        }
        else
        {
            await WriteNewAtomicallyAsync(destinationPath, bytes, cancellationToken);
        }

        var savedBytes = await _fileSystem.ReadAllBytesAsync(destinationPath, cancellationToken);
        return new DocumentSourceSnapshot
        {
            SourcePath = destinationPath,
            LoadedAtUtc = DateTimeOffset.UtcNow,
            LastWriteTimeUtc = _fileSystem.GetLastWriteTimeUtc(destinationPath),
            Length = savedBytes.LongLength,
            ContentHash = Convert.ToHexString(SHA256.HashData(savedBytes)),
        };
    }

    private async Task WriteNewAtomicallyAsync(
        string destinationPath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{destinationPath}.ziap-tmp";
        try
        {
            await _fileSystem.WriteAllBytesWithFlushAsync(temporaryPath, bytes, cancellationToken);
            using var validation = JsonDocument.Parse(
                await _fileSystem.ReadAllBytesAsync(temporaryPath, cancellationToken));
            if (_fileSystem.FileExists(destinationPath))
            {
                throw new ExternalDocumentModificationException(
                    $"{Path.GetFileName(destinationPath)} è stato creato esternamente.");
            }
            _fileSystem.MoveFile(temporaryPath, destinationPath, overwrite: false);
        }
        finally
        {
            if (_fileSystem.FileExists(temporaryPath))
            {
                _fileSystem.DeleteFile(temporaryPath);
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadSystemSoundsAsync(
        ZiapProject project,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(project.Path, "data", "System.json");
        if (!_fileSystem.FileExists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(
            await _fileSystem.ReadAllBytesAsync(path, cancellationToken));
        if (!document.RootElement.TryGetProperty("sounds", out var sounds) ||
            sounds.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var slots = FusionAudioSystemSounds.Slots;
        var index = 0;
        foreach (var sound in sounds.EnumerateArray())
        {
            if (index >= slots.Count) break;
            if (sound.ValueKind == JsonValueKind.Object &&
                sound.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String)
            {
                result[slots[index]] = name.GetString() ?? string.Empty;
            }
            index++;
        }
        return result;
    }

    private FusionAudioResolvedAsset ResolveAsset(
        ZiapProject project,
        string eventId,
        string rawName,
        string sourceKind,
        string? systemSlot)
    {
        var normalized = rawName.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized) || normalized.Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is "." or ".."))
        {
            return new FusionAudioResolvedAsset
            {
                EventId = eventId,
                AssetName = rawName,
                RelativePath = rawName,
                SourceKind = sourceKind,
                SystemSlot = systemSlot,
                Exists = false,
            };
        }

        var relativeBase = Path.Combine("audio", "se", normalized);
        var declaredExtension = Path.GetExtension(relativeBase);
        var candidates = AudioExtensions.Contains(
            declaredExtension,
            StringComparer.OrdinalIgnoreCase)
            ? [relativeBase]
            : AudioExtensions.Select(extension => relativeBase + extension).ToArray();
        var resolved = candidates
            .Select(candidate => Path.GetFullPath(Path.Combine(project.Path, candidate)))
            .FirstOrDefault(_fileSystem.FileExists);
        var relative = resolved is null
            ? candidates[0]
            : Path.GetRelativePath(project.Path, resolved);
        return new FusionAudioResolvedAsset
        {
            EventId = eventId,
            AssetName = rawName,
            RelativePath = relative.Replace('\\', '/'),
            ResolvedPath = resolved,
            SourceKind = sourceKind,
            SystemSlot = systemSlot,
            Exists = resolved is not null,
        };
    }

    private static void ValidateEntry(
        string eventId,
        FusionAudioEntry entry,
        ICollection<FusionAudioDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            diagnostics.Add(Error("sound.id", "Un evento non ha un identificatore."));
            return;
        }
        if (!eventId.Contains('.'))
        {
            diagnostics.Add(Warning(
                "sound.id-format",
                "L'identificatore dovrebbe seguire il formato categoria.evento.",
                eventId));
        }
        if (entry.Volume is < 0 or > 100)
        {
            diagnostics.Add(Error("sound.volume", "Il volume deve essere compreso tra 0 e 100.", eventId));
        }
        if (entry.Pitch.Min > entry.Pitch.Max)
        {
            diagnostics.Add(Error("sound.pitch-order", "Il pitch minimo supera il massimo.", eventId));
        }
        if (entry.Pitch.Min < 50 || entry.Pitch.Max > 150)
        {
            diagnostics.Add(Warning(
                "sound.pitch-range",
                $"Intervallo pitch insolito: {entry.Pitch.Min}–{entry.Pitch.Max}.",
                eventId));
        }
        if (entry.Pan is < -100 or > 100)
        {
            diagnostics.Add(Error("sound.pan", "Il pan deve essere compreso tra -100 e 100.", eventId));
        }
        if (entry.Cooldown < 0)
        {
            diagnostics.Add(Error("sound.cooldown", "Il cooldown non può essere negativo.", eventId));
        }

        var kind = entry.Source.Kind?.Trim() ?? string.Empty;
        if (kind.Equals(FusionAudioSourceKinds.Files, StringComparison.OrdinalIgnoreCase))
        {
            if (entry.Source.Files.Count == 0 || entry.Source.Files.All(string.IsNullOrWhiteSpace))
            {
                diagnostics.Add(Error("sound.files-empty", "Nessun asset configurato.", eventId));
            }
            if (entry.Source.Files.Count != entry.Source.Files
                .Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                diagnostics.Add(Warning("sound.files-duplicate", "La stessa variante compare più volte.", eventId));
            }
        }
        else if (!kind.Equals(FusionAudioSourceKinds.SystemSound, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error("sound.source-kind", $"Tipo sorgente sconosciuto: '{kind}'.", eventId));
        }
    }

    private static bool IsUnitValue(double value) => value is >= 0 and <= 1;

    private static int GetAudioExtensionPriority(string extension)
    {
        for (var index = 0; index < AudioExtensions.Length; index++)
        {
            if (AudioExtensions[index].Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }
        return AudioExtensions.Length;
    }

    private static FusionAudioDiagnostic Error(string code, string message, string? eventId = null) =>
        new()
        {
            Code = code,
            Severity = FusionAudioDiagnosticSeverity.Error,
            Message = message,
            EventId = eventId,
        };

    private static FusionAudioDiagnostic Warning(string code, string message, string? eventId = null) =>
        new()
        {
            Code = code,
            Severity = FusionAudioDiagnosticSeverity.Warning,
            Message = message,
            EventId = eventId,
        };
}
