using ZiapStudio.Core.Documents;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public async Task ResolveAsync_ResolvesFusionArrayAndObjectPaths()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "locales/it/db.json",
            """[{ "moneteAbisso": "Moneta dell'Abisso" }]""");
        workspace.WriteFile(
            "locales/it/main.json",
            """
            [{}, {}, {}, {}, {
              "strings": [{}, {}, { "items": { "weapon": "Arma" } }]
            }]
            """);
        var service = CreateService();

        var database = await service.ResolveAsync(
            workspace.RootPath,
            "{db[0].moneteAbisso}");
        var main = await service.ResolveAsync(
            workspace.RootPath,
            "{main[4].strings[2].items.weapon}");

        Assert.NotNull(database);
        Assert.Equal("Moneta dell'Abisso", database.ResolvedValue);
        Assert.Equal("db[0].moneteAbisso", database.Target);
        Assert.NotNull(database.Origin);
        Assert.Equal("db", database.Origin.Namespace);
        Assert.Equal("0.moneteAbisso", database.Origin.Path);
        Assert.Equal("it", database.Origin.Locale);
        Assert.Equal("db.json", database.Origin.SourceFile);
        Assert.Equal(2, database.Origin.Segments.Count);
        Assert.Equal(0, database.Origin.Segments[0].ArrayIndex);
        Assert.Equal("moneteAbisso", database.Origin.Segments[1].PropertyName);
        Assert.Equal("Moneta dell'Abisso", database.Origin.ResolvedValue);
        Assert.Equal(RpgMakerResolutionStatus.Resolved, database.Status);
        Assert.NotNull(main);
        Assert.Equal("Arma", main.ResolvedValue);
        Assert.NotNull(main.Origin);
        Assert.Equal("main", main.Origin.Namespace);
        Assert.Equal("4.strings.2.items.weapon", main.Origin.Path);
        Assert.Equal("main.json", main.Origin.SourceFile);
        Assert.Equal(RpgMakerResolutionStatus.Resolved, main.Status);
    }

    [Fact]
    public async Task ResolveAsync_ReportsMissingLocalizationTarget()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("locales/it/db.json", "[{}]");
        var service = CreateService();

        var resolution = await service.ResolveAsync(
            workspace.RootPath,
            "{db[0].missing}");

        Assert.NotNull(resolution);
        Assert.Null(resolution.ResolvedValue);
        Assert.Equal("db[0].missing", resolution.Target);
        Assert.NotNull(resolution.Origin);
        Assert.Equal("0.missing", resolution.Origin.Path);
        Assert.Equal("db.json", resolution.Origin.SourceFile);
        Assert.Equal(RpgMakerResolutionStatus.MissingTarget, resolution.Status);
    }

    [Fact]
    public async Task ResolveAsync_UsesFusionNestedNamespaceRegistryForMdv()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "locales/it/dialogue/mdv.json",
            """
            [{"DestinyOfBirth":[{"newPrologoStory":[{"text":"Testo localizzato"}]}]}]
            """);

        var resolution = await CreateService().ResolveAsync(
            workspace.RootPath,
            "{mdv[0].DestinyOfBirth[0].newPrologoStory[0].text}");

        Assert.NotNull(resolution);
        Assert.Equal("Testo localizzato", resolution.ResolvedValue);
        Assert.Equal("dialogue/mdv.json", resolution.Origin!.SourceFile);
        Assert.Equal("0.DestinyOfBirth.0.newPrologoStory.0.text", resolution.Origin.Path);
        Assert.Collection(
            resolution.Origin.Segments,
            segment => Assert.Equal(0, segment.ArrayIndex),
            segment => Assert.Equal("DestinyOfBirth", segment.PropertyName),
            segment => Assert.Equal(0, segment.ArrayIndex),
            segment => Assert.Equal("newPrologoStory", segment.PropertyName),
            segment => Assert.Equal(0, segment.ArrayIndex),
            segment => Assert.Equal("text", segment.PropertyName));
    }

    [Fact]
    public async Task Invalidate_RefreshesOnlyTheWrittenLocalizationDocument()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("locales/it/dialogue/mdv.json", "[{\"text\":\"A\"}]");
        var service = CreateService();

        var before = await service.ResolveAsync(workspace.RootPath, "{mdv[0].text}");
        workspace.WriteFile("locales/it/dialogue/mdv.json", "[{\"text\":\"B\"}]");
        var cached = await service.ResolveAsync(workspace.RootPath, "{mdv[0].text}");
        service.Invalidate(workspace.RootPath, "it", "dialogue/mdv.json");
        var refreshed = await service.ResolveAsync(workspace.RootPath, "{mdv[0].text}");

        Assert.Equal("A", before!.ResolvedValue);
        Assert.Equal("A", cached!.ResolvedValue);
        Assert.Equal("B", refreshed!.ResolvedValue);
    }

    [Fact]
    public async Task ResolveAsync_IgnoresPrimitiveStrings()
    {
        using var workspace = new TestWorkspace();

        var resolution = await CreateService().ResolveAsync(
            workspace.RootPath,
            "Testo normale");

        Assert.Null(resolution);
    }

    private static LocalizationService CreateService()
    {
        var fileSystem = new FileSystemService();
        return new LocalizationService(
        [
            new FusionLocalizationProvider(fileSystem),
        ]);
    }
}
