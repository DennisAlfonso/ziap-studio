using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Localization;

namespace ZiapStudio.Services.Fusion.Story;

/// <summary>Produces only the canonical 101/401 pair for a committed MDV append.</summary>
public static class StoryRpgMakerCommandGenerator
{
    public static IReadOnlyList<StoryGeneratedCommand> CreatePreviewCommands(StoryCompositionPlan plan) =>
    [
        new StoryGeneratedCommand
        {
            Code = 101,
            Indent = plan.Indent,
            Parameters =
            [
                plan.Presentation.FaceName,
                plan.Presentation.FaceIndex,
                plan.Presentation.Background,
                plan.Presentation.PositionType,
                plan.OperationType == StoryCompositionOperationType.AddDialogue ? "{… .name}" : string.Empty,
            ],
        },
        new StoryGeneratedCommand { Code = 401, Indent = plan.Indent, Parameters = ["{… .text}"] },
    ];

    public static IReadOnlyList<StoryGeneratedCommand> CreateCommittedCommands(
        StoryCompositionPlan plan,
        IReadOnlyList<LocalizationPathSegment> entryPath)
    {
        var namePath = entryPath.Append(LocalizationPathSegment.Property("name")).ToArray();
        var textPath = entryPath.Append(LocalizationPathSegment.Property("text")).ToArray();
        return
        [
            new StoryGeneratedCommand
            {
                Code = 101,
                Indent = plan.Indent,
                Parameters =
                [
                    plan.Presentation.FaceName,
                    plan.Presentation.FaceIndex,
                    plan.Presentation.Background,
                    plan.Presentation.PositionType,
                    plan.OperationType == StoryCompositionOperationType.AddDialogue
                        ? SerializeReference(plan.Namespace, namePath)
                        : string.Empty,
                ],
            },
            new StoryGeneratedCommand
            {
                Code = 401,
                Indent = plan.Indent,
                Parameters = [SerializeReference(plan.Namespace, textPath)],
            },
        ];
    }

    public static string SerializeReference(string @namespace, IReadOnlyList<LocalizationPathSegment> segments)
    {
        if (string.IsNullOrWhiteSpace(@namespace) || segments.Count == 0)
        {
            throw new StoryCompositionValidationException("Reference Localization non serializzabile.");
        }
        var suffix = string.Concat(segments.Select(segment => segment.ArrayIndex is int index
            ? $"[{index}]"
            : segment.PropertyName is { Length: > 0 } name ? $".{name}"
            : throw new StoryCompositionValidationException("Segmento Localization non valido.")));
        return $"{{{@namespace}{suffix}}}";
    }
}
