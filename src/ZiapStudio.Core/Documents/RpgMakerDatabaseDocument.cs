using System.Text.Json.Nodes;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Weapons;
using ZiapStudio.Core.Localization;

namespace ZiapStudio.Core.Documents;

public sealed record RpgMakerDatabaseDocument : StudioDocument
{
    public string SourcePath { get; init; } = string.Empty;

    public RpgMakerDatabaseDefinition Definition { get; init; } = new();

    public IReadOnlyList<RpgMakerDatabaseEntry> Entries { get; init; } = [];

    public JsonNode SourceRoot { get; init; } = new JsonArray();

    public DocumentSourceSnapshot SourceSnapshot { get; init; } = new();

    public WeaponNotetagCatalog? WeaponNotetagCatalog { get; init; }
}

public sealed record RpgMakerDatabaseEntry
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, RpgMakerResolvedValue> Values { get; init; } =
        new Dictionary<string, RpgMakerResolvedValue>(StringComparer.OrdinalIgnoreCase);

    public RpgMakerResolvedValue GetValue(string key) =>
        Values.TryGetValue(key, out var value)
            ? value
            : RpgMakerResolvedValue.Empty;
}

public sealed record RpgMakerResolvedValue
{
    public static RpgMakerResolvedValue Empty { get; } = new();

    public string RawValue { get; init; } = string.Empty;

    public string? ResolvedValue { get; init; }

    public string DisplayValue { get; init; } = string.Empty;

    public RpgMakerResolvedValueKind Kind { get; init; }

    public string? ReferenceTarget { get; init; }

    public string? Target { get; init; }

    public RpgMakerResolutionStatus Status { get; init; }

    public LocalizationReferenceOrigin? LocalizationOrigin { get; init; }

    public IReadOnlyList<RpgMakerReferenceOption> EditorOptions { get; init; } = [];

    public bool IsResolved => Status == RpgMakerResolutionStatus.Resolved;
}

public sealed record RpgMakerReferenceOption(int Value, string DisplayValue);

public enum RpgMakerResolvedValueKind
{
    Primitive,
    Text,
    Note,
    DatabaseReference,
    SystemReference,
    LocalizationReference,
    AssetReference,
}

public enum RpgMakerResolutionStatus
{
    NotApplicable,
    Resolved,
    Unresolved,
    MissingTarget,
}
