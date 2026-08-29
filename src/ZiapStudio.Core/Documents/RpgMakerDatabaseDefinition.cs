namespace ZiapStudio.Core.Documents;

public sealed record RpgMakerDatabaseDefinition
{
    public string ResourceName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string ItemDisplayName { get; init; } = "Elemento";

    public IReadOnlyList<RpgMakerDatabaseColumnDefinition> Columns { get; init; } = [];

    public IReadOnlyList<RpgMakerDatabaseSectionDefinition> Sections { get; init; } = [];
}

public sealed record RpgMakerDatabaseColumnDefinition
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public double Width { get; init; } = 100;

    public RpgMakerValuePresentation Presentation { get; init; }

    public string? ReferenceTarget { get; init; }
}

public sealed record RpgMakerDatabaseSectionDefinition
{
    public string DisplayName { get; init; } = string.Empty;

    public IReadOnlyList<RpgMakerDatabaseFieldDefinition> Fields { get; init; } = [];
}

public sealed record RpgMakerDatabaseFieldDefinition
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public RpgMakerValuePresentation Presentation { get; init; }

    public string? ReferenceTarget { get; init; }

    public RpgMakerEditorKind EditorKind { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public bool IsEditable => EditorKind != RpgMakerEditorKind.ReadOnly;
}

public enum RpgMakerEditorKind
{
    ReadOnly,
    Text,
    MultilineText,
    Number,
    ReferenceComboBox,
}

public enum RpgMakerValuePresentation
{
    Primitive,
    Text,
    Note,
    DatabaseReference,
    SystemReference,
    AssetReference,
}
