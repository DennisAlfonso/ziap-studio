using ZiapStudio.Core.Assets;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Assets;

namespace ZiapStudio.Services.Tests;

public sealed class AssetResolutionTests
{
    [Fact]
    public async Task IconSet_ResolvesIconIdAndCreatesCorrectCrop()
    {
        using var workspace = new TestWorkspace();
        var iconSetPath = workspace.WritePngHeader("img/system/IconSet.png", 512, 3392);
        var (resolver, previewProvider) = CreateServices();

        var reference = resolver.Resolve(
            CreateProject(workspace.RootPath),
            CreateEntry(("iconIndex", AssetValue("203", "icons"))),
            "iconIndex",
            AssetValue("203", "icons"));
        var preview = await previewProvider.CreatePreviewAsync(reference);

        Assert.Equal(AssetResolutionStatus.Resolved, reference.Status);
        Assert.Equal(RpgMakerAssetKind.Icon, reference.AssetKind);
        Assert.Equal(iconSetPath, reference.ResolvedPath);
        Assert.Equal("img/system/IconSet.png", reference.RelativePath);
        Assert.Equal(203, reference.Index);
        Assert.NotNull(preview);
        Assert.Equal((352, 384, 32, 32),
            (preview.CropX, preview.CropY, preview.CropWidth, preview.CropHeight));
    }

    [Fact]
    public async Task FaceSheet_UsesFaceIndexToCreateCrop()
    {
        using var workspace = new TestWorkspace();
        workspace.WritePngHeader("img/faces/YatoBase.png", 576, 288);
        var (resolver, previewProvider) = CreateServices();
        var entry = CreateEntry(
            ("faceName", AssetValue("YatoBase", "faces")),
            ("faceIndex", PrimitiveValue("5")));

        var reference = resolver.Resolve(
            CreateProject(workspace.RootPath),
            entry,
            "faceName",
            entry.GetValue("faceName"));
        var preview = await previewProvider.CreatePreviewAsync(reference);

        Assert.Equal(5, reference.Index);
        Assert.NotNull(preview);
        Assert.Equal((144, 144, 144, 144),
            (preview.CropX, preview.CropY, preview.CropWidth, preview.CropHeight));
    }

    [Fact]
    public async Task BigCharacterSheet_UsesStandingDownFrame()
    {
        using var workspace = new TestWorkspace();
        workspace.WritePngHeader("img/characters/$YatoBase.png", 144, 288);
        var (resolver, previewProvider) = CreateServices();
        var entry = CreateEntry(
            ("characterName", AssetValue("$YatoBase", "characters")),
            ("characterIndex", PrimitiveValue("0")));

        var reference = resolver.Resolve(
            CreateProject(workspace.RootPath),
            entry,
            "characterName",
            entry.GetValue("characterName"));
        var preview = await previewProvider.CreatePreviewAsync(reference);

        Assert.NotNull(preview);
        Assert.Equal((48, 0, 48, 72),
            (preview.CropX, preview.CropY, preview.CropWidth, preview.CropHeight));
    }

    [Fact]
    public async Task StandardCharacterSheet_UsesCharacterIndexAndStandingDownFrame()
    {
        using var workspace = new TestWorkspace();
        workspace.WritePngHeader("img/characters/Heroes.png", 576, 384);
        var (resolver, previewProvider) = CreateServices();
        var entry = CreateEntry(
            ("characterName", AssetValue("Heroes", "characters")),
            ("characterIndex", PrimitiveValue("5")));

        var reference = resolver.Resolve(
            CreateProject(workspace.RootPath),
            entry,
            "characterName",
            entry.GetValue("characterName"));
        var preview = await previewProvider.CreatePreviewAsync(reference);

        Assert.NotNull(preview);
        Assert.Equal((192, 192, 48, 48),
            (preview.CropX, preview.CropY, preview.CropWidth, preview.CropHeight));
    }

    [Fact]
    public async Task EnemyBattler_FallsBackToSideViewFolderAndUsesFullImage()
    {
        using var workspace = new TestWorkspace();
        var battlerPath = workspace.WritePngHeader("img/sv_enemies/Boss.png", 900, 600);
        var (resolver, previewProvider) = CreateServices();
        var value = AssetValue("Boss", "enemies");

        var reference = resolver.Resolve(
            CreateProject(workspace.RootPath),
            CreateEntry(("battlerName", value)),
            "battlerName",
            value);
        var preview = await previewProvider.CreatePreviewAsync(reference);

        Assert.Equal(AssetResolutionStatus.Resolved, reference.Status);
        Assert.Equal(battlerPath, reference.ResolvedPath);
        Assert.Equal("img/sv_enemies/Boss.png", reference.RelativePath);
        Assert.NotNull(preview);
        Assert.Equal((0, 0, 900, 600),
            (preview.CropX, preview.CropY, preview.CropWidth, preview.CropHeight));
    }

    [Fact]
    public async Task MissingAsset_ProducesDiagnosticWithoutPreview()
    {
        using var workspace = new TestWorkspace();
        var (resolver, previewProvider) = CreateServices();
        var value = AssetValue("MissingFace", "faces");

        var reference = resolver.Resolve(
            CreateProject(workspace.RootPath),
            CreateEntry(("faceName", value), ("faceIndex", PrimitiveValue("0"))),
            "faceName",
            value);
        var preview = await previewProvider.CreatePreviewAsync(reference);

        Assert.Equal(AssetResolutionStatus.Missing, reference.Status);
        Assert.Equal("img/faces/MissingFace.png", reference.RelativePath);
        Assert.Contains("Asset mancante", reference.Diagnostic);
        Assert.Null(preview);
    }

    [Fact]
    public void AssetName_CannotEscapeProjectFolder()
    {
        using var workspace = new TestWorkspace();
        var (resolver, _) = CreateServices();
        var value = AssetValue("../../outside", "faces");

        var reference = resolver.Resolve(
            CreateProject(workspace.RootPath),
            CreateEntry(("faceName", value), ("faceIndex", PrimitiveValue("0"))),
            "faceName",
            value);

        Assert.Equal(AssetResolutionStatus.Invalid, reference.Status);
        Assert.Contains("non è valido", reference.Diagnostic);
    }

    private static (AssetResolver Resolver, RpgMakerAssetPreviewProvider PreviewProvider)
        CreateServices()
    {
        var fileSystem = new FileSystemService();
        return (
            new AssetResolver(new RpgMakerAssetProvider(fileSystem)),
            new RpgMakerAssetPreviewProvider(fileSystem));
    }

    private static ZiapProject CreateProject(string path) => new()
    {
        Id = "project",
        Name = "Project",
        ProjectType = KnownProjectTypes.RpgMakerMz,
        Path = path,
    };

    private static RpgMakerDatabaseEntry CreateEntry(
        params (string Key, RpgMakerResolvedValue Value)[] values) => new()
    {
        Id = 1,
        Values = values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase),
    };

    private static RpgMakerResolvedValue AssetValue(string rawValue, string target) => new()
    {
        RawValue = rawValue,
        DisplayValue = rawValue,
        Kind = RpgMakerResolvedValueKind.AssetReference,
        ReferenceTarget = target,
        Status = RpgMakerResolutionStatus.Unresolved,
    };

    private static RpgMakerResolvedValue PrimitiveValue(string rawValue) => new()
    {
        RawValue = rawValue,
        DisplayValue = rawValue,
        Kind = RpgMakerResolvedValueKind.Primitive,
        Status = RpgMakerResolutionStatus.NotApplicable,
    };
}
