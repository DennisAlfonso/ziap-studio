using ZiapStudio.Core.Assets;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Assets;

public sealed class AssetPreviewService
{
    private readonly AssetResolver _assetResolver;
    private readonly RpgMakerAssetPreviewProvider _previewProvider;

    public AssetPreviewService(
        AssetResolver assetResolver,
        RpgMakerAssetPreviewProvider previewProvider)
    {
        _assetResolver = assetResolver;
        _previewProvider = previewProvider;
    }

    public async Task<IReadOnlyDictionary<AssetPreviewKey, AssetPreviewResult>>
        CreatePreviewsAsync(
            ZiapProject project,
            RpgMakerDatabaseDocument document,
            CancellationToken cancellationToken = default)
    {
        var assetFields = document.Definition.Sections
            .SelectMany(section => section.Fields)
            .Where(field => field.Presentation == RpgMakerValuePresentation.AssetReference)
            .DistinctBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var results = new Dictionary<AssetPreviewKey, AssetPreviewResult>();

        foreach (var entry in document.Entries)
        {
            foreach (var field in assetFields)
            {
                var value = entry.GetValue(field.Key);
                var reference = _assetResolver.Resolve(project, entry, field.Key, value);
                results[new AssetPreviewKey(entry.Id, field.Key)] = new AssetPreviewResult
                {
                    Reference = reference,
                    Preview = await _previewProvider.CreatePreviewAsync(reference, cancellationToken),
                };
            }
        }

        return results;
    }
}
