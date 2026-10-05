namespace ZiapStudio.Services.ProjectSafety;

/// <summary>Ownership policy for files below an opened game project.</summary>
public enum ProjectFileOwnership
{
    RpgMakerOwned,
    SharedProject,
    StudioOwned,
    OutsideProject,
}

/// <summary>
/// Centralizes ownership rules. Feature services must not reimplement these
/// path checks because the process-coexistence policy depends on them.
/// </summary>
public sealed class ProjectFileClassifier
{
    public ProjectFileOwnership Classify(string projectRoot, string targetPath)
    {
        var root = ProjectPathSafety.NormalizeProjectRoot(projectRoot);
        var target = ProjectPathSafety.NormalizeContainedPath(root, targetPath);
        var relative = Path.GetRelativePath(root, target)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 || segments[0].Equals("..", StringComparison.Ordinal))
        {
            return ProjectFileOwnership.OutsideProject;
        }

        if (segments[0].Equals(".ziap", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileOwnership.StudioOwned;
        }

        if (segments.Length == 1 && segments[0].Equals("game.rmmzproject", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileOwnership.RpgMakerOwned;
        }

        if (segments.Length == 2 &&
            segments[0].Equals("js", StringComparison.OrdinalIgnoreCase) &&
            segments[1].Equals("plugins.js", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileOwnership.RpgMakerOwned;
        }

        // Only direct data/*.json belongs to RPG Maker. data/fusion and other
        // custom subtrees deliberately remain shared with their own hash guard.
        if (segments.Length == 2 &&
            segments[0].Equals("data", StringComparison.OrdinalIgnoreCase) &&
            segments[1].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileOwnership.RpgMakerOwned;
        }

        return ProjectFileOwnership.SharedProject;
    }
}

internal static class ProjectPathSafety
{
    public static string NormalizeProjectRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.InvalidProjectPath,
                "La root del progetto non è valida.");
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        EnsureNoReparsePoints(normalized, normalized);
        return normalized;
    }

    public static string NormalizeContainedPath(string normalizedProjectRoot, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.InvalidProjectPath,
                "Il percorso di destinazione non è valido.");
        }

        var target = Path.GetFullPath(targetPath);
        var prefix = normalizedProjectRoot + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.InvalidProjectPath,
                "La destinazione è esterna al progetto aperto.");
        }

        EnsureNoReparsePoints(normalizedProjectRoot, target);
        return target;
    }

    private static void EnsureNoReparsePoints(string root, string target)
    {
        var rootAttributes = File.GetAttributes(root);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.InvalidProjectPath,
                "La root del progetto non può essere un reparse point.");
        }

        var relative = Path.GetRelativePath(root, target);
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                break;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ProjectWriteException(
                    ProjectWriteFailure.InvalidProjectPath,
                    "Il percorso di destinazione attraversa un reparse point.");
            }
        }
    }
}
