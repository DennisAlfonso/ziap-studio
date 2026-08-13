using ZiapStudio.Core.Documents;

namespace ZiapStudio.Services.Documents;

internal static class RpgMakerDatabaseDefinitions
{
    private static readonly IReadOnlyDictionary<string, RpgMakerDatabaseDefinition> Definitions =
        new Dictionary<string, RpgMakerDatabaseDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["weapons"] = new()
            {
                ResourceName = "weapons",
                DisplayName = "Armi",
                ItemDisplayName = "Arma",
                Columns =
                [
                    Column("id", "ID", 50),
                    Column("name", "Nome", 190, RpgMakerValuePresentation.Text),
                    Column("wtypeId", "Tipo arma", 100, RpgMakerValuePresentation.SystemReference, "weaponTypes"),
                    Column("price", "Prezzo", 75),
                ],
                Sections =
                [
                    Section("Generale",
                        Field("id", "ID"),
                        Field("name", "Nome", RpgMakerValuePresentation.Text),
                        Field("description", "Descrizione", RpgMakerValuePresentation.Text)),
                    Section("Equipaggiamento",
                        Field("wtypeId", "Tipo arma", RpgMakerValuePresentation.SystemReference, "weaponTypes", RpgMakerEditorKind.ReferenceComboBox),
                        Field("etypeId", "Slot equipaggiamento", RpgMakerValuePresentation.SystemReference, "equipTypes", RpgMakerEditorKind.ReferenceComboBox),
                        Field("iconIndex", "Icon ID", RpgMakerValuePresentation.AssetReference, "icons", RpgMakerEditorKind.Number, minimum: 0),
                        Field("animationId", "Animazione", RpgMakerValuePresentation.DatabaseReference, "animations", RpgMakerEditorKind.ReferenceComboBox),
                        Field("price", "Prezzo", editorKind: RpgMakerEditorKind.Number, minimum: 0)),
                    Section("Parametri",
                        Field("params[0]", "HP massimi", editorKind: RpgMakerEditorKind.Number),
                        Field("params[1]", "MP massimi", editorKind: RpgMakerEditorKind.Number),
                        Field("params[2]", "Attacco", editorKind: RpgMakerEditorKind.Number),
                        Field("params[3]", "Difesa", editorKind: RpgMakerEditorKind.Number),
                        Field("params[4]", "Attacco magico", editorKind: RpgMakerEditorKind.Number),
                        Field("params[5]", "Difesa magica", editorKind: RpgMakerEditorKind.Number),
                        Field("params[6]", "Agilità", editorKind: RpgMakerEditorKind.Number),
                        Field("params[7]", "Fortuna", editorKind: RpgMakerEditorKind.Number)),
                    Section("Note", Field("note", "Note", RpgMakerValuePresentation.Note)),
                ],
            },
            ["actors"] = new()
            {
                ResourceName = "actors",
                DisplayName = "Attori",
                ItemDisplayName = "Attore",
                Columns =
                [
                    Column("id", "ID", 50),
                    Column("name", "Nome", 180, RpgMakerValuePresentation.Text),
                    Column("classId", "Classe", 80, RpgMakerValuePresentation.DatabaseReference, "classes"),
                    Column("initialLevel", "Livello iniziale", 105),
                ],
                Sections =
                [
                    Section("Generale",
                        Field("id", "ID"),
                        Field("name", "Nome", RpgMakerValuePresentation.Text),
                        Field("nickname", "Soprannome", RpgMakerValuePresentation.Text),
                        Field("classId", "Classe", RpgMakerValuePresentation.DatabaseReference, "classes"),
                        Field("profile", "Profilo", RpgMakerValuePresentation.Text)),
                    Section("Progressione",
                        Field("initialLevel", "Livello iniziale"),
                        Field("maxLevel", "Livello massimo")),
                    Section("Grafica",
                        Field("characterName", "Character", RpgMakerValuePresentation.AssetReference, "characters"),
                        Field("characterIndex", "Character index"),
                        Field("faceName", "Face", RpgMakerValuePresentation.AssetReference, "faces"),
                        Field("faceIndex", "Face index"),
                        Field("battlerName", "Battler", RpgMakerValuePresentation.AssetReference, "sv-actors")),
                    Section("Note", Field("note", "Note", RpgMakerValuePresentation.Note)),
                ],
            },
            ["enemies"] = new()
            {
                ResourceName = "enemies",
                DisplayName = "Nemici",
                ItemDisplayName = "Nemico",
                Columns =
                [
                    Column("id", "ID", 50),
                    Column("name", "Nome", 195, RpgMakerValuePresentation.Text),
                    Column("exp", "EXP", 75),
                    Column("gold", "Gold", 75),
                ],
                Sections =
                [
                    Section("Generale",
                        Field("id", "ID"),
                        Field("name", "Nome", RpgMakerValuePresentation.Text),
                        Field("battlerName", "Battler", RpgMakerValuePresentation.AssetReference, "enemies"),
                        Field("battlerHue", "Tonalità battler")),
                    Section("Ricompense",
                        Field("exp", "EXP"),
                        Field("gold", "Gold")),
                    Section("Statistiche",
                        Field("params[0]", "HP massimi"),
                        Field("params[1]", "MP massimi"),
                        Field("params[2]", "Attacco"),
                        Field("params[3]", "Difesa"),
                        Field("params[4]", "Attacco magico"),
                        Field("params[5]", "Difesa magica"),
                        Field("params[6]", "Agilità"),
                        Field("params[7]", "Fortuna")),
                    Section("Note", Field("note", "Note", RpgMakerValuePresentation.Note)),
                ],
            },
        };

    public static RpgMakerDatabaseDefinition Get(string resourceName, string displayName) =>
        Definitions.TryGetValue(resourceName, out var definition)
            ? definition with { DisplayName = displayName }
            : CreateGeneric(resourceName, displayName);

    private static RpgMakerDatabaseDefinition CreateGeneric(
        string resourceName,
        string displayName) => new()
    {
        ResourceName = resourceName,
        DisplayName = displayName,
        ItemDisplayName = "Elemento",
        Columns =
        [
            Column("id", "ID", 50),
            Column("name", "Nome", 300, RpgMakerValuePresentation.Text),
        ],
        Sections =
        [
            Section("Generale",
                Field("id", "ID"),
                Field("name", "Nome", RpgMakerValuePresentation.Text),
                Field("description", "Descrizione", RpgMakerValuePresentation.Text)),
            Section("Note", Field("note", "Note", RpgMakerValuePresentation.Note)),
        ],
    };

    private static RpgMakerDatabaseColumnDefinition Column(
        string key,
        string displayName,
        double width,
        RpgMakerValuePresentation presentation = RpgMakerValuePresentation.Primitive,
        string? referenceTarget = null) => new()
    {
        Key = key,
        DisplayName = displayName,
        Width = width,
        Presentation = presentation,
        ReferenceTarget = referenceTarget,
    };

    private static RpgMakerDatabaseSectionDefinition Section(
        string displayName,
        params RpgMakerDatabaseFieldDefinition[] fields) => new()
    {
        DisplayName = displayName,
        Fields = fields,
    };

    private static RpgMakerDatabaseFieldDefinition Field(
        string key,
        string displayName,
        RpgMakerValuePresentation presentation = RpgMakerValuePresentation.Primitive,
        string? referenceTarget = null,
        RpgMakerEditorKind editorKind = RpgMakerEditorKind.ReadOnly,
        double? minimum = null,
        double? maximum = null) => new()
    {
        Key = key,
        DisplayName = displayName,
        Presentation = presentation,
        ReferenceTarget = referenceTarget,
        EditorKind = editorKind,
        Minimum = minimum,
        Maximum = maximum,
    };
}
