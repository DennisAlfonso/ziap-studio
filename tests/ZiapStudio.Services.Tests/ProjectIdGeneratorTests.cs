using ZiapStudio.Services.Initialization;

namespace ZiapStudio.Services.Tests;

public sealed class ProjectIdGeneratorTests
{
    private readonly ProjectIdGenerator _generator = new();

    [Theory]
    [InlineData("Fusion: Hexella Dive", "fusion-hexella-dive")]
    [InlineData("ZIAP Editor", "ziap-editor")]
    [InlineData("Project NEXUS", "project-nexus")]
    [InlineData("myZenkai", "myzenkai")]
    [InlineData("  Crème  brûlée! ", "creme-brulee")]
    [InlineData("---", "project")]
    public void Generate_CreatesStableSlug(string name, string expectedId)
    {
        Assert.Equal(expectedId, _generator.Generate(name));
    }

    [Theory]
    [InlineData("fusion-hexella-dive", true)]
    [InlineData("myzenkai", true)]
    [InlineData("Fusion-Hexella-Dive", false)]
    [InlineData("double--dash", false)]
    [InlineData(" leading", false)]
    [InlineData("", false)]
    public void IsValid_EnforcesProjectIdFormat(string projectId, bool expected)
    {
        Assert.Equal(expected, _generator.IsValid(projectId));
    }
}
