using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using ZiapStudio.Core.Assets;

namespace ZiapStudio.ViewModels;

public sealed class AssetPreviewViewModel
{
    public AssetPreviewViewModel(AssetPreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Preview is null)
        {
            throw new ArgumentException("Il risultato non contiene un'anteprima.", nameof(result));
        }

        var preview = result.Preview;
        var maximumSize = result.Reference.AssetKind == RpgMakerAssetKind.Icon ? 72d : 128d;
        var maximumScale = result.Reference.AssetKind == RpgMakerAssetKind.Icon ? 2.25d : 1.5d;
        Scale = Math.Min(
            maximumScale,
            Math.Min(maximumSize / preview.CropWidth, maximumSize / preview.CropHeight));

        try
        {
            using var fileStream = File.OpenRead(preview.SourcePath);
            using var randomAccessStream = fileStream.AsRandomAccessStream();
            var imageSource = new BitmapImage();
            imageSource.SetSource(randomAccessStream);
            ImageSource = imageSource;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or COMException)
        {
            LoadDiagnostic = "Il file esiste, ma Windows non può caricare l'anteprima.";
        }
        ViewportWidth = preview.CropWidth * Scale;
        ViewportHeight = preview.CropHeight * Scale;
        ImageWidth = preview.SourceWidth * Scale;
        ImageHeight = preview.SourceHeight * Scale;
        OffsetX = -preview.CropX * Scale;
        OffsetY = -preview.CropY * Scale;
        ClipRect = new Rect(0, 0, ViewportWidth, ViewportHeight);
        AccessibleName = $"Anteprima asset {result.Reference.RawValue}";
    }

    public BitmapImage? ImageSource { get; }

    public string? LoadDiagnostic { get; }

    public double Scale { get; }

    public double ViewportWidth { get; }

    public double ViewportHeight { get; }

    public double ImageWidth { get; }

    public double ImageHeight { get; }

    public double OffsetX { get; }

    public double OffsetY { get; }

    public Rect ClipRect { get; }

    public string AccessibleName { get; }
}
