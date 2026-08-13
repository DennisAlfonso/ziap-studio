using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ZiapStudio.Services.Initialization;

public sealed class ProjectIdGenerator
{
    private static readonly Regex ValidIdPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Generate(string projectName)
    {
        ArgumentNullException.ThrowIfNull(projectName);

        var normalizedName = projectName.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalizedName.Length);
        var separatorPending = false;

        foreach (var character in normalizedName)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character <= 127 && char.IsLetterOrDigit(character))
            {
                if (separatorPending && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                separatorPending = false;
            }
            else
            {
                separatorPending = builder.Length > 0;
            }
        }

        return builder.Length == 0 ? "project" : builder.ToString();
    }

    public bool IsValid(string projectId) =>
        !string.IsNullOrWhiteSpace(projectId) && ValidIdPattern.IsMatch(projectId);
}
