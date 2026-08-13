using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Metadata;

internal sealed class ProjectMetadataResolver
{
    private readonly IReadOnlyList<IProjectMetadataSource> _sources;

    public ProjectMetadataResolver(IReadOnlyList<IProjectMetadataSource> sources)
    {
        _sources = sources;
    }

    public async Task<ZiapProject> ResolveAsync(
        ProjectDetection detection,
        CancellationToken cancellationToken)
    {
        var contributions = new List<ProjectMetadataContribution>(_sources.Count);
        foreach (var source in _sources)
        {
            var contribution = await source.ReadAsync(detection, cancellationToken);
            if (contribution is not null)
            {
                contributions.Add(contribution);
            }
        }

        return new ZiapProject
        {
            Id = FirstRequired(contributions, contribution => contribution.Id),
            Name = FirstRequired(contributions, contribution => contribution.Name),
            PackageName = FirstOptional(contributions, contribution => contribution.PackageName),
            Version = FirstOptional(contributions, contribution => contribution.Version),
            ProjectType = FirstOptional(contributions, contribution => contribution.ProjectType)
                ?? KnownProjectTypes.Unknown,
            Publisher = FirstOptional(contributions, contribution => contribution.Publisher),
            Path = detection.ProjectPath,
            IsZiapInitialized = detection.HasZiapMetadata,
        };
    }

    private static string FirstRequired(
        IEnumerable<ProjectMetadataContribution> contributions,
        Func<ProjectMetadataContribution, string?> selector) =>
        FirstOptional(contributions, selector)
        ?? throw new InvalidOperationException("Il resolver non ha prodotto un'identità di progetto.");

    private static string? FirstOptional(
        IEnumerable<ProjectMetadataContribution> contributions,
        Func<ProjectMetadataContribution, string?> selector)
    {
        foreach (var contribution in contributions)
        {
            var value = selector(contribution);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
