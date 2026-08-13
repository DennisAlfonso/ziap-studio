using ZiapStudio.Core.Assets;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Assets;

public sealed class AssetResolver
{
    private readonly RpgMakerAssetProvider _rpgMakerProvider;

    public AssetResolver(RpgMakerAssetProvider rpgMakerProvider)
    {
        _rpgMakerProvider = rpgMakerProvider;
    }

    public AssetReference Resolve(
        ZiapProject project,
        RpgMakerDatabaseEntry entry,
        string fieldKey,
        RpgMakerResolvedValue value)
    {
        if (_rpgMakerProvider.CanResolve(value))
        {
            try
            {
                return _rpgMakerProvider.Resolve(project, entry, fieldKey, value);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return new AssetReference
                {
                    AssetKind = RpgMakerAssetKind.Image,
                    RawValue = value.RawValue,
                    Status = AssetResolutionStatus.Invalid,
                    Diagnostic = "Il percorso dell'asset non è valido.",
                };
            }
        }

        return new AssetReference
        {
            AssetKind = RpgMakerAssetKind.Image,
            RawValue = value.RawValue,
            Status = AssetResolutionStatus.Invalid,
            Diagnostic = "Nessun provider può risolvere questo asset.",
        };
    }
}
