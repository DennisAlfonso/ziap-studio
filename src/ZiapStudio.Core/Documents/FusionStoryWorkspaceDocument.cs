using ZiapStudio.Core.Fusion.Story;

namespace ZiapStudio.Core.Documents;

public sealed record FusionStoryWorkspaceDocument : StudioDocument
{
    public string ProjectPath { get; init; } = string.Empty;
    public StoryWorkspace Workspace { get; init; } = new();

    public int PageCount => Workspace.Maps.Sum(map => map.Events.Sum(@event => @event.Pages.Count));
    public int BlockCount => Workspace.Maps.Sum(map => map.Events.Sum(@event =>
        @event.Pages.Sum(page => page.Blocks.Count))) + Workspace.CommonEvents.Sum(@event => @event.Blocks.Count);
    public int ErrorCount => Workspace.Diagnostics.Count(diagnostic => diagnostic.Severity == StoryDiagnosticSeverity.Error);
    public int WarningCount => Workspace.Diagnostics.Count(diagnostic => diagnostic.Severity == StoryDiagnosticSeverity.Warning);
}
