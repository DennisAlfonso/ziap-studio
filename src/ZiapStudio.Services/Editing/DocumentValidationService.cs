using System.Text.Json;
using System.Text.Json.Nodes;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Services.Fusion.Weapons;

namespace ZiapStudio.Services.Editing;

public sealed class DocumentValidationService
{
    public DocumentValidationResult Validate(DocumentEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var issues = new List<DocumentValidationIssue>(session.BaselineIssues);

        if (session.WorkingState is not JsonArray records)
        {
            issues.Add(Error(
                "invalid-root",
                "Il database RPG Maker deve essere un array JSON."));
            return new DocumentValidationResult { Issues = issues };
        }

        if (records.Count == 0 || records[0] is not null)
        {
            issues.Add(Error(
                "missing-null-sentinel",
                "L'elemento iniziale null del database RPG Maker deve essere preservato."));
        }

        for (var index = 1; index < records.Count; index++)
        {
            if (records[index] is null)
            {
                continue;
            }

            if (records[index] is not JsonObject record)
            {
                issues.Add(Error(
                    "invalid-record",
                    $"L'elemento all'indice {index} non è un oggetto o null.",
                    propertyPath: $"[{index}]"));
                continue;
            }

            if (record["id"] is not JsonValue idValue ||
                !idValue.TryGetValue<int>(out var id))
            {
                issues.Add(Error(
                    "invalid-id",
                    $"Il record all'indice {index} non contiene un ID numerico.",
                    propertyPath: $"[{index}].id"));
            }
            else if (id != index)
            {
                issues.Add(Error(
                    "incompatible-id",
                    $"Il record all'indice {index} dichiara l'ID {id}.",
                    target: $"{session.Descriptor.ResourceId.AbsoluteUri.TrimEnd('/')}/{id}",
                    propertyPath: "id"));
            }
        }

        foreach (var change in session.ChangeSet.Changes)
        {
            if (!change.OldValueExists || change.OldValue is null || change.NewValue is null)
            {
                continue;
            }

            var oldKind = change.OldValue.GetValueKind();
            var newKind = change.NewValue.GetValueKind();
            if (oldKind != newKind)
            {
                issues.Add(Error(
                    "incompatible-value-type",
                    $"{change.PropertyPath} cambia tipo da {Describe(oldKind)} a {Describe(newKind)}.",
                    change.Target,
                    change.PropertyPath));
            }

            if (session.Definition.ResourceName.Equals("weapons", StringComparison.OrdinalIgnoreCase) &&
                change.PropertyPath.Equals("note", StringComparison.OrdinalIgnoreCase) &&
                change.NewValue is JsonValue noteValue &&
                noteValue.TryGetValue<string>(out var note))
            {
                var metadata = new WeaponAdvancedMetadataProvider().Parse(
                    note,
                    session.WeaponNotetagCatalog);
                foreach (var diagnostic in metadata.Diagnostics.Where(diagnostic =>
                    diagnostic.Severity != WeaponNotetagDiagnosticSeverity.Information))
                {
                    issues.Add(new DocumentValidationIssue
                    {
                        Severity = diagnostic.Severity == WeaponNotetagDiagnosticSeverity.Error
                            ? DocumentValidationSeverity.Error
                            : DocumentValidationSeverity.Warning,
                        Code = diagnostic.Severity == WeaponNotetagDiagnosticSeverity.Error
                            ? "invalid-weapon-notetag"
                            : "weapon-notetag-warning",
                        Message = diagnostic.Message,
                        Target = change.Target,
                        PropertyPath = change.PropertyPath,
                    });
                }
            }

            var definition = session.Definition.Sections
                .SelectMany(section => section.Fields)
                .FirstOrDefault(field => string.Equals(
                    field.Key,
                    change.PropertyPath,
                    StringComparison.OrdinalIgnoreCase));
            if (definition is null ||
                definition.EditorKind != RpgMakerEditorKind.Number ||
                !TryGetNumber(change.NewValue, out var numericValue))
            {
                continue;
            }

            if (definition.Minimum is not null && numericValue < definition.Minimum.Value)
            {
                issues.Add(Error(
                    "value-below-minimum",
                    $"{definition.DisplayName} deve essere almeno {definition.Minimum.Value}.",
                    change.Target,
                    change.PropertyPath));
            }

            if (definition.Maximum is not null && numericValue > definition.Maximum.Value)
            {
                issues.Add(Error(
                    "value-above-maximum",
                    $"{definition.DisplayName} deve essere al massimo {definition.Maximum.Value}.",
                    change.Target,
                    change.PropertyPath));
            }
        }

        return new DocumentValidationResult { Issues = issues };
    }

    private static bool TryGetNumber(JsonNode node, out double value)
    {
        value = 0;
        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<int>(out var intValue))
        {
            value = intValue;
            return true;
        }

        if (jsonValue.TryGetValue<long>(out var longValue))
        {
            value = longValue;
            return true;
        }

        if (jsonValue.TryGetValue<double>(out var doubleValue))
        {
            value = doubleValue;
            return true;
        }

        return false;
    }

    private static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.String => "testo",
        JsonValueKind.Number => "numero",
        JsonValueKind.True or JsonValueKind.False => "booleano",
        JsonValueKind.Array => "array",
        JsonValueKind.Object => "oggetto",
        JsonValueKind.Null => "null",
        _ => kind.ToString(),
    };

    private static DocumentValidationIssue Error(
        string code,
        string message,
        string? target = null,
        string? propertyPath = null) => new()
    {
        Severity = DocumentValidationSeverity.Error,
        Code = code,
        Message = message,
        Target = target,
        PropertyPath = propertyPath,
    };
}
