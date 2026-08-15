using ZiapStudio.Core.Preflight;
using ZiapStudio.Services.Fusion.Weapons;

namespace ZiapStudio.Services.Fusion.Preflight.Weapons;

public static class WeaponPreflightProfile
{
    public static IReadOnlyList<IPreflightRule<WeaponPreflightContext>> CreateRules() =>
    [
        new MissingWeaponMetadataRule(
            "weapon.rarity.missing",
            PreflightSeverity.Error,
            context => context.Metadata.HasRarityTag,
            "itemRare non è definito."),
        new MissingWeaponMetadataRule(
            "weapon.perk.missing",
            PreflightSeverity.Warning,
            context => context.Metadata.HasPerkTag,
            "perk non è definito."),
        new MissingWeaponMetadataRule(
            "weapon.required-level.missing",
            PreflightSeverity.Warning,
            context => context.Metadata.HasRequiredLevelTag,
            "lvReq non è definito."),
        new WeaponDiagnosticRule("weapon.rarity.invalid", PreflightSeverity.Error),
        new WeaponDiagnosticRule("weapon.perk.columns", PreflightSeverity.Error),
        new WeaponPerkCatalogRule(),
        new WeaponDiagnosticRule("weapon.required-level.invalid", PreflightSeverity.Error),
        new WeaponDiagnosticRule("weapon.maximum-level.invalid", PreflightSeverity.Error),
        new WeaponDiagnosticRule("weapon.maximum-level.required-override", PreflightSeverity.Error),
        new WeaponDiagnosticRule("weapon.custom-parameter.malformed", PreflightSeverity.Error),
        new UnknownCustomParameterRule(),
        new WeaponDiagnosticRule("weapon.lore.unresolved", PreflightSeverity.Warning),
        new WeaponDiagnosticRule("weapon.disassembly.syntax", PreflightSeverity.Error),
        new WeaponDisassemblyResourceRule(),
        new WeaponDiagnosticRule("weapon.disassembly.range", PreflightSeverity.Error),
        new WeaponDiagnosticRule("weapon.disassembly.probability", PreflightSeverity.Error),
    ];
}

internal sealed class MissingWeaponMetadataRule : IPreflightRule<WeaponPreflightContext>
{
    private readonly PreflightSeverity _severity;
    private readonly Func<WeaponPreflightContext, bool> _isPresent;
    private readonly string _message;

    public MissingWeaponMetadataRule(
        string ruleId,
        PreflightSeverity severity,
        Func<WeaponPreflightContext, bool> isPresent,
        string message)
    {
        RuleId = ruleId;
        _severity = severity;
        _isPresent = isPresent;
        _message = message;
    }

    public string RuleId { get; }

    public IEnumerable<PreflightIssue> Evaluate(WeaponPreflightContext context)
    {
        if (!_isPresent(context))
        {
            yield return context.CreateIssue(RuleId, _severity, _message);
        }
    }
}

internal sealed class WeaponDiagnosticRule : IPreflightRule<WeaponPreflightContext>
{
    private readonly PreflightSeverity _severity;

    public WeaponDiagnosticRule(string ruleId, PreflightSeverity severity)
    {
        RuleId = ruleId;
        _severity = severity;
    }

    public string RuleId { get; }

    public IEnumerable<PreflightIssue> Evaluate(WeaponPreflightContext context) =>
        context.Metadata.Diagnostics
            .Where(diagnostic => diagnostic.Code?.Equals(
                RuleId,
                StringComparison.OrdinalIgnoreCase) == true)
            .Select(diagnostic => context.CreateIssue(RuleId, _severity, diagnostic.Message))
            .GroupBy(issue => issue.Message, StringComparer.Ordinal)
            .Select(group => group.First());
}

internal sealed class UnknownCustomParameterRule : IPreflightRule<WeaponPreflightContext>
{
    public string RuleId => "weapon.custom-parameter.unknown";

    public IEnumerable<PreflightIssue> Evaluate(WeaponPreflightContext context)
    {
        foreach (var parameter in context.Metadata.CustomParameters.Where(parameter =>
                     context.Catalog.CustomParameters.All(option => option.Id != parameter.Id)))
        {
            yield return context.CreateIssue(
                RuleId,
                PreflightSeverity.Warning,
                $"Il parametro custom cp[{parameter.Id}] non è presente nel catalogo del progetto.");
        }
    }
}

internal sealed class WeaponPerkCatalogRule : IPreflightRule<WeaponPreflightContext>
{
    public string RuleId => "weapon.perk.catalog";

    public IEnumerable<PreflightIssue> Evaluate(WeaponPreflightContext context)
    {
        if (!context.Metadata.HasPerkTag || context.Metadata.Perks.Count != 3)
        {
            yield break;
        }

        for (var index = 0; index < context.Metadata.Perks.Count; index++)
        {
            var value = context.Metadata.Perks[index];
            var column = index + 1;
            if (context.Catalog.Perks.Any(option => option.Column == column &&
                option.Id.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var existsElsewhere = context.Catalog.Perks.Any(option =>
                option.Id.Equals(value, StringComparison.OrdinalIgnoreCase));
            var ruleId = existsElsewhere ? "weapon.perk.column" : "weapon.perk.unknown";
            yield return context.CreateIssue(
                ruleId,
                PreflightSeverity.Error,
                existsElsewhere
                    ? $"Il perk {value} appartiene a una colonna diversa dalla {column}."
                    : $"Il perk {value} non esiste nel catalogo del progetto.");
        }
    }
}

internal sealed class WeaponDisassemblyResourceRule : IPreflightRule<WeaponPreflightContext>
{
    public string RuleId => "weapon.disassembly.resource";

    public IEnumerable<PreflightIssue> Evaluate(WeaponPreflightContext context)
    {
        foreach (var result in context.Metadata.DisassemblyResults.Where(result =>
                     context.Catalog.DisassemblyResources.All(option =>
                         !option.RawValue.Equals(result.RawResource, StringComparison.OrdinalIgnoreCase))))
        {
            yield return context.CreateIssue(
                RuleId,
                PreflightSeverity.Error,
                $"La risorsa di disassemblaggio {result.RawResource} non esiste nel catalogo del progetto.");
        }
    }
}
