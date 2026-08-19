using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Bosses;

namespace ZiapStudio.ViewModels;

public sealed class FusionBossDocumentViewModel
{
    public FusionBossDocumentViewModel(FusionBossWorkspaceDocument document)
    {
        Document = document;
        Databases = document.Databases
            .Select(database => new FusionBossDatabaseViewModel(database))
            .ToArray();
        Plugins = document.Plugins
            .Select(plugin => new FusionBossPluginViewModel(plugin))
            .ToArray();
        Diagnostics = document.Diagnostics
            .Select(diagnostic => new FusionBossDiagnosticViewModel(diagnostic))
            .ToArray();
    }

    public FusionBossWorkspaceDocument Document { get; }

    public IReadOnlyList<FusionBossDatabaseViewModel> Databases { get; }

    public IReadOnlyList<FusionBossPluginViewModel> Plugins { get; }

    public IReadOnlyList<FusionBossDiagnosticViewModel> Diagnostics { get; }

    public string StatusText => Document.IsValid
        ? "Contratti validi"
        : $"{Document.ErrorCount} errori";

    public string WarningText => $"{Document.WarningCount} avvisi";

    public string BossCountText => $"{Document.GetRecordCount("bosses")} boss";

    public string EncounterCountText => $"{Document.GetRecordCount("encounters")} encounter";

    public string ArenaCountText => $"{Document.GetRecordCount("arenas")} arene";

    public bool HasDiagnostics => Diagnostics.Count > 0;

    public string DiagnosticsTitle => Diagnostics.Count == 0
        ? "Nessun problema rilevato"
        : $"Diagnostica ({Diagnostics.Count})";
}

public sealed class FusionBossDatabaseViewModel
{
    public FusionBossDatabaseViewModel(FusionBossDatabaseSummary database)
    {
        Database = database;
    }

    public FusionBossDatabaseSummary Database { get; }

    public string DisplayName => Database.DisplayName;

    public string StatusGlyph => Database.Exists ? "●" : "○";

    public string VersionText => Database.Exists
        ? $"schema {Database.SchemaVersion?.ToString() ?? "?"} · db {Database.DatabaseVersion ?? "?"}"
        : "File mancante";

    public string RecordCountText => $"{Database.RecordCount} record";

    public string CollectionsText => Database.Collections.Count == 0
        ? "Nessuna collezione disponibile"
        : string.Join(
            " · ",
            Database.Collections.Select(collection =>
                $"{collection.DisplayName}: {collection.RecordIds.Count}"));

    public string SourceFileName => Path.GetFileName(Database.SourcePath);
}

public sealed class FusionBossPluginViewModel
{
    public FusionBossPluginViewModel(FusionBossPluginStatus plugin)
    {
        Plugin = plugin;
    }

    public FusionBossPluginStatus Plugin { get; }

    public string DisplayName => Plugin.DisplayName;

    public string StatusGlyph => Plugin.IsActive ? "●" : "○";

    public string StatusText => Plugin.IsActive
        ? "Attivo"
        : Plugin.IsRequired ? "Richiesto" : "Non attivo";
}

public sealed class FusionBossDiagnosticViewModel
{
    public FusionBossDiagnosticViewModel(FusionBossDiagnostic diagnostic)
    {
        Diagnostic = diagnostic;
    }

    public FusionBossDiagnostic Diagnostic { get; }

    public string Glyph => Diagnostic.Severity switch
    {
        FusionBossDiagnosticSeverity.Error => "●",
        FusionBossDiagnosticSeverity.Warning => "▲",
        _ => "○",
    };

    public string Message => Diagnostic.Message;

    public string ContextText
    {
        get
        {
            var parts = new[]
            {
                Diagnostic.Database?.ToString(),
                Diagnostic.CollectionId,
                Diagnostic.RecordId,
                Diagnostic.Code,
            }.Where(value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" · ", parts);
        }
    }
}
