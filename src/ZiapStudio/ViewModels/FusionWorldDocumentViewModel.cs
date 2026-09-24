using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media.Imaging;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.World;

namespace ZiapStudio.ViewModels;

public sealed class FusionWorldDocumentViewModel : INotifyPropertyChanged
{
    private FusionWorldTileset? _selectedTileset;
    private FusionWorldAsset? _selectedAsset;
    private FusionWorldMap? _selectedMap;
    private FusionWorldRegion? _selectedRegion;
    private BitmapImage? _selectedAssetPreview;

    public FusionWorldDocumentViewModel(FusionWorldWorkspaceDocument document)
    {
        Document = document;
        SelectedTileset = Tilesets.FirstOrDefault(tileset => tileset.IsInUse) ?? Tilesets.FirstOrDefault();
        SelectedAsset = Assets.FirstOrDefault(asset => asset.Exists && !asset.IsOrphan) ?? Assets.FirstOrDefault();
        SelectedMap = Maps.FirstOrDefault();
        SelectedRegion = Regions.FirstOrDefault(region => region.CellCount > 0) ?? Regions.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FusionWorldWorkspaceDocument Document { get; }

    public IReadOnlyList<FusionWorldTileset> Tilesets => Document.Tilesets;

    public IReadOnlyList<FusionWorldAsset> Assets => Document.Assets;

    public IReadOnlyList<FusionWorldMap> Maps => Document.Maps;

    public IReadOnlyList<FusionWorldRegion> Regions => Document.Regions;

    public IReadOnlyList<FusionWorldDiagnostic> Diagnostics => Document.Diagnostics;

    public string WorkspaceStatusText => Document.ErrorCount > 0
        ? $"{Document.ErrorCount} errori · {Document.WarningCount} avvisi"
        : $"{Document.UsedTilesetCount} tileset in uso · nessun errore";

    public string InventoryText =>
        $"{Document.UsedTilesetCount} tileset in uso · {Assets.Count} asset · " +
        $"{Document.OrganizedCategoryCount} categorie";

    public string RegionRegistryText => Document.RegionRegistryExists
        ? $"{Document.RegisteredRegionCount} Region registrate · {Document.ActiveRegionCount} in uso"
        : $"Registry assente · {Document.ActiveRegionCount} Region in uso";

    public string AssetAuditText =>
        $"{Document.MissingAssetCount} mancanti · {Document.OrphanAssetCount} orfani · " +
        $"{Document.DuplicateAssetCount} duplicati";

    public FusionWorldTileset? SelectedTileset
    {
        get => _selectedTileset;
        set
        {
            if (ReferenceEquals(_selectedTileset, value))
            {
                return;
            }
            _selectedTileset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedTilesetSlots));
            OnPropertyChanged(nameof(SelectedTilesetMaps));
            OnPropertyChanged(nameof(SelectedTilesetStatusText));
        }
    }

    public IReadOnlyList<FusionWorldTilesetSlot> SelectedTilesetSlots =>
        SelectedTileset?.Slots ?? [];

    public IReadOnlyList<FusionWorldMapUsage> SelectedTilesetMaps =>
        SelectedTileset?.Maps ?? [];

    public string SelectedTilesetStatusText => SelectedTileset is null
        ? "Seleziona un tileset"
        : $"{SelectedTileset.MapCountText} · {SelectedTileset.AssetCount} sheet · " +
          $"{SelectedTileset.MissingAssetCount} mancanti";

    public FusionWorldAsset? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (ReferenceEquals(_selectedAsset, value))
            {
                return;
            }
            _selectedAsset = value;
            SelectedAssetPreview = CreatePreview(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAssetTilesets));
            OnPropertyChanged(nameof(SelectedAssetMaps));
            OnPropertyChanged(nameof(SelectedAssetStatusText));
        }
    }

    public IReadOnlyList<FusionWorldTileset> SelectedAssetTilesets =>
        SelectedAsset?.Tilesets ?? [];

    public IReadOnlyList<FusionWorldMapUsage> SelectedAssetMaps =>
        SelectedAsset?.Maps ?? [];

    public BitmapImage? SelectedAssetPreview
    {
        get => _selectedAssetPreview;
        private set
        {
            if (ReferenceEquals(_selectedAssetPreview, value))
            {
                return;
            }
            _selectedAssetPreview = value;
            OnPropertyChanged();
        }
    }

    public string SelectedAssetStatusText => SelectedAsset is null
        ? "Seleziona un asset"
        : $"{SelectedAsset.StatusText} · {SelectedAsset.UsageText} · {SelectedAsset.MapUsageText}";

    public FusionWorldMap? SelectedMap
    {
        get => _selectedMap;
        set
        {
            if (ReferenceEquals(_selectedMap, value))
            {
                return;
            }
            _selectedMap = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMapStatusText));
        }
    }

    public string SelectedMapStatusText => SelectedMap is null
        ? "Seleziona una mappa"
        : $"{SelectedMap.IdText} · {SelectedMap.TilesetText} · " +
          $"{SelectedMap.Width} × {SelectedMap.Height} tile";

    public FusionWorldRegion? SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (ReferenceEquals(_selectedRegion, value))
            {
                return;
            }
            _selectedRegion = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedRegionUsages));
            OnPropertyChanged(nameof(SelectedRegionStatusText));
        }
    }

    public IReadOnlyList<FusionWorldRegionUsage> SelectedRegionUsages =>
        SelectedRegion?.Usages ?? [];

    public string SelectedRegionStatusText => SelectedRegion is null
        ? "Seleziona una Region"
        : $"{SelectedRegion.StatusText} · {SelectedRegion.MapCountText} · " +
          $"{SelectedRegion.CellCountText}";

    private static BitmapImage? CreatePreview(FusionWorldAsset? asset)
    {
        if (asset is not { Exists: true } || !File.Exists(asset.PhysicalPath))
        {
            return null;
        }
        try
        {
            return new BitmapImage(new Uri(asset.PhysicalPath, UriKind.Absolute));
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
