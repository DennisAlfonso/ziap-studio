using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Localization;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Integration.Remote;

/// <summary>
/// Determines whether Story authoring may begin without creating or changing a
/// Localization session. A lock owned by another author is represented by a
/// session without an edit session and is intentionally not editable.
/// </summary>
public static class StoryLocalizationAuthoringAvailability
{
    public static bool CanBeginEditing(
        bool isAuthoringBusy,
        StoryBlock? selectedBlock,
        StoryLocalizationAuthoringSession? authoringSession)
    {
        if (isAuthoringBusy || !IsAuthoringContent(selectedBlock) ||
            selectedBlock!.LocalizationOrigins.Any(IsMasterEditable) != true)
        {
            return false;
        }

        if (authoringSession is null)
        {
            return true;
        }

        if (authoringSession.EditSession is not { } editSession)
        {
            return false;
        }

        return !editSession.IsDirty || selectedBlock.LocalizationOrigins.Any(origin =>
            IsSameFile(origin, authoringSession.Origin));
    }

    private static bool IsMasterEditable(LocalizationReferenceOrigin origin) =>
        origin.Locale.Equals(LocalizationService.DefaultLocale, StringComparison.OrdinalIgnoreCase) &&
        origin.Segments.Count > 0;

    /// <summary>
    /// A reference in a comment/plugin/script is readable metadata, not permission
    /// to edit that command through the narrative master-text workflow.
    /// </summary>
    private static bool IsAuthoringContent(StoryBlock? block) =>
        block?.Kind is StoryBlockKind.Dialogue or StoryBlockKind.Choices;

    private static bool IsSameFile(LocalizationReferenceOrigin left, LocalizationReferenceOrigin right) =>
        left.Locale.Equals(right.Locale, StringComparison.OrdinalIgnoreCase) &&
        left.SourceFile.Equals(right.SourceFile, StringComparison.OrdinalIgnoreCase);
}
