using ZiapStudio.Core.Assets;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;

namespace ZiapStudio.Services.Editing;

public sealed class DocumentEditSessionFactory
{
    public DocumentEditSession Create(
        RpgMakerDatabaseDocument document,
        IReadOnlyDictionary<AssetPreviewKey, AssetPreviewResult>? assetPreviews = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = CreateReferenceIssues(document, assetPreviews);
        return new DocumentEditSession(document, issues);
    }

    private static IReadOnlyList<DocumentValidationIssue> CreateReferenceIssues(
        RpgMakerDatabaseDocument document,
        IReadOnlyDictionary<AssetPreviewKey, AssetPreviewResult>? assetPreviews)
    {
        var issues = new List<DocumentValidationIssue>();
        foreach (var entry in document.Entries)
        {
            foreach (var (propertyPath, value) in entry.Values)
            {
                if (value.Status != RpgMakerResolutionStatus.MissingTarget)
                {
                    continue;
                }

                var (code, label) = value.Kind switch
                {
                    RpgMakerResolvedValueKind.LocalizationReference =>
                        ("missing-localization", "Localizzazione mancante"),
                    RpgMakerResolvedValueKind.DatabaseReference =>
                        ("missing-database-reference", "Riferimento database mancante"),
                    RpgMakerResolvedValueKind.SystemReference =>
                        ("missing-system-reference", "Riferimento System mancante"),
                    _ => ("missing-reference", "Riferimento mancante"),
                };
                issues.Add(new DocumentValidationIssue
                {
                    Severity = DocumentValidationSeverity.Warning,
                    Code = code,
                    Message = $"{label}: record #{entry.Id}, {propertyPath} = {value.RawValue}.",
                    Target = $"{document.Descriptor.ResourceId.AbsoluteUri.TrimEnd('/')}/{entry.Id}",
                    PropertyPath = propertyPath,
                });
            }
        }

        if (assetPreviews is null)
        {
            return issues;
        }

        foreach (var (key, result) in assetPreviews)
        {
            if (result.Reference.Status is not (
                AssetResolutionStatus.Missing or AssetResolutionStatus.Invalid))
            {
                continue;
            }

            issues.Add(new DocumentValidationIssue
            {
                Severity = DocumentValidationSeverity.Warning,
                Code = result.Reference.Status == AssetResolutionStatus.Missing
                    ? "missing-asset"
                    : "invalid-asset",
                Message = result.Reference.Diagnostic ??
                    $"Asset non risolto: {result.Reference.RawValue}.",
                Target = $"{document.Descriptor.ResourceId.AbsoluteUri.TrimEnd('/')}/{key.EntryId}",
                PropertyPath = key.FieldKey,
            });
        }

        return issues;
    }
}
