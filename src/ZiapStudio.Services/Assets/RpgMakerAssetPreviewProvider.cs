using System.Buffers.Binary;
using System.Collections.Concurrent;
using ZiapStudio.Core.Assets;

namespace ZiapStudio.Services.Assets;

public sealed class RpgMakerAssetPreviewProvider
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly FileSystemService _fileSystem;
    private readonly ConcurrentDictionary<string, Task<ImageSize?>> _imageSizes =
        new(StringComparer.OrdinalIgnoreCase);

    public RpgMakerAssetPreviewProvider(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<AssetPreviewDescriptor?> CreatePreviewAsync(
        AssetReference reference,
        CancellationToken cancellationToken = default)
    {
        if (reference.Status != AssetResolutionStatus.Resolved ||
            string.IsNullOrWhiteSpace(reference.ResolvedPath))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var size = await _imageSizes.GetOrAdd(
            reference.ResolvedPath,
            path => ReadPngSizeAsync(path, CancellationToken.None));
        if (size is null)
        {
            return null;
        }

        var crop = CreateCrop(reference, size.Value);
        if (crop is null ||
            crop.Value.X < 0 || crop.Value.Y < 0 ||
            crop.Value.Width <= 0 || crop.Value.Height <= 0 ||
            crop.Value.X + crop.Value.Width > size.Value.Width ||
            crop.Value.Y + crop.Value.Height > size.Value.Height)
        {
            return null;
        }

        return new AssetPreviewDescriptor
        {
            SourcePath = reference.ResolvedPath,
            SourceWidth = size.Value.Width,
            SourceHeight = size.Value.Height,
            CropX = crop.Value.X,
            CropY = crop.Value.Y,
            CropWidth = crop.Value.Width,
            CropHeight = crop.Value.Height,
        };
    }

    private async Task<ImageSize?> ReadPngSizeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var header = await _fileSystem.ReadBytesAsync(path, 24, cancellationToken);
            if (header.Length < 24 || !header.AsSpan(0, 8).SequenceEqual(PngSignature))
            {
                return null;
            }

            var width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
            return width > 0 && height > 0 ? new ImageSize(width, height) : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static CropRectangle? CreateCrop(AssetReference reference, ImageSize size) =>
        reference.AssetKind switch
        {
            RpgMakerAssetKind.Icon => CreateIconCrop(reference.Index, size),
            RpgMakerAssetKind.Face => CreateFaceCrop(reference.Index, size),
            RpgMakerAssetKind.Character => CreateCharacterCrop(
                reference.RawValue,
                reference.Index,
                size),
            _ => new CropRectangle(0, 0, size.Width, size.Height),
        };

    private static CropRectangle? CreateIconCrop(int? index, ImageSize size)
    {
        const int iconSize = 32;
        var columnCount = size.Width / iconSize;
        if (index is null || index < 0 || columnCount <= 0)
        {
            return null;
        }

        return new CropRectangle(
            index.Value % columnCount * iconSize,
            index.Value / columnCount * iconSize,
            iconSize,
            iconSize);
    }

    private static CropRectangle? CreateFaceCrop(int? index, ImageSize size)
    {
        if (index is null || index is < 0 or > 7 || size.Width % 4 != 0 || size.Height % 2 != 0)
        {
            return null;
        }

        var width = size.Width / 4;
        var height = size.Height / 2;
        return new CropRectangle(
            index.Value % 4 * width,
            index.Value / 4 * height,
            width,
            height);
    }

    private static CropRectangle? CreateCharacterCrop(
        string rawValue,
        int? index,
        ImageSize size)
    {
        if (rawValue.StartsWith('$'))
        {
            return size.Width % 3 == 0 && size.Height % 4 == 0
                ? new CropRectangle(size.Width / 3, 0, size.Width / 3, size.Height / 4)
                : null;
        }

        if (index is null || index is < 0 or > 7 || size.Width % 12 != 0 || size.Height % 8 != 0)
        {
            return null;
        }

        var frameWidth = size.Width / 12;
        var frameHeight = size.Height / 8;
        var characterColumn = index.Value % 4;
        var characterRow = index.Value / 4;
        return new CropRectangle(
            (characterColumn * 3 + 1) * frameWidth,
            characterRow * 4 * frameHeight,
            frameWidth,
            frameHeight);
    }

    private readonly record struct ImageSize(int Width, int Height);

    private readonly record struct CropRectangle(int X, int Y, int Width, int Height);
}
