using System.Text.Json;
using System.Text.RegularExpressions;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Puzzles;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Integrations;

namespace ZiapStudio.Services.Fusion.Puzzles;

public sealed partial class FusionPuzzleWorkspaceService
{
    private static readonly PluginSpec[] PluginSpecs =
    [
        new(FusionPuzzleIntegrationProvider.CorePluginName, "Core Puzzle", true),
        new(FusionPuzzleIntegrationProvider.MovementPluginName, "Movement bridge", false),
        new(FusionPuzzleIntegrationProvider.HexellaWeightPluginName, "Hexella Weight", false),
        new(FusionBossIntegrationProvider.ArenaPluginName, "Arena host", false),
    ];

    private readonly FileSystemService _fileSystem;
    private readonly RpgMakerPluginRegistryService _pluginRegistry;

    public FusionPuzzleWorkspaceService(
        FileSystemService fileSystem,
        RpgMakerPluginRegistryService pluginRegistry)
    {
        _fileSystem = fileSystem;
        _pluginRegistry = pluginRegistry;
    }

    public async Task<FusionPuzzleWorkspaceDocument> LoadAsync(
        ZiapProject project,
        DocumentDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(descriptor);

        var registrations = await _pluginRegistry.LoadAsync(project, cancellationToken);
        var plugins = PluginSpecs.Select(spec => new FusionPuzzlePluginStatus
        {
            Id = spec.Id,
            DisplayName = spec.DisplayName,
            IsRequired = spec.IsRequired,
            IsActive = IsPluginActive(registrations, spec.Id),
        }).ToArray();
        var diagnostics = plugins
            .Where(plugin => plugin.IsRequired && !plugin.IsActive)
            .Select(plugin => Issue(
                "plugin.required-inactive",
                FusionPuzzleDiagnosticSeverity.Error,
                string.Empty,
                $"Il plugin richiesto {plugin.Id} non e attivo.",
                "Il runtime non puo registrare o preparare alcun puzzle."))
            .ToList();

        var databasePath = Path.Combine(project.Path, "data", "FusionPuzzles.json");
        if (!_fileSystem.FileExists(databasePath))
        {
            diagnostics.Add(Issue(
                "database.missing",
                FusionPuzzleDiagnosticSeverity.Error,
                string.Empty,
                "FusionPuzzles.json non e presente.",
                databasePath));
            return CreateDocument(
                project,
                descriptor,
                databasePath,
                plugins,
                diagnostics,
                [],
                null,
                null);
        }

        using var database = JsonDocument.Parse(await _fileSystem.ReadAllTextAsync(
            databasePath,
            cancellationToken));
        var root = database.RootElement;
        var schemaVersion = ReadInt(root, "schemaVersion");
        var databaseVersion = ReadString(root, "databaseVersion");
        if (!root.TryGetProperty("puzzles", out var puzzleCollection) ||
            puzzleCollection.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Issue(
                "database.puzzles-missing",
                FusionPuzzleDiagnosticSeverity.Error,
                string.Empty,
                "La raccolta puzzles non e disponibile.",
                "FusionPuzzles.json deve contenere un oggetto 'puzzles'."));
            return CreateDocument(
                project,
                descriptor,
                databasePath,
                plugins,
                diagnostics,
                [],
                schemaVersion,
                databaseVersion);
        }

        var mapNames = await LoadMapNamesAsync(project, cancellationToken);
        var components = await ScanMapComponentsAsync(project, mapNames, diagnostics, cancellationToken);
        var usages = await LoadArenaUsagesAsync(project, diagnostics, cancellationToken);
        var providerMetadata = ReadProviderMetadata(root);
        plugins = plugins
            .Concat(providerMetadata.Values.SelectMany(metadata =>
            {
                var providerPlugins = string.IsNullOrWhiteSpace(metadata.PluginId)
                    ? Array.Empty<FusionPuzzlePluginStatus>()
                    :
                    [
                        new FusionPuzzlePluginStatus
                        {
                            Id = metadata.PluginId,
                            DisplayName = metadata.DisplayName,
                            IsRequired = false,
                            IsActive = IsPluginActive(registrations, metadata.PluginId),
                        },
                    ];
                return providerPlugins.Concat(metadata.Dependencies.Select(id =>
                    new FusionPuzzlePluginStatus
                    {
                        Id = id,
                        DisplayName = id,
                        IsRequired = false,
                        IsActive = IsPluginActive(registrations, id),
                    }));
            }))
            .GroupBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var puzzles = new List<FusionPuzzleDefinition>();
        foreach (var property in puzzleCollection.EnumerateObject())
        {
            var providerId = ReadString(property.Value, "type") ??
                ReadString(property.Value, "provider") ?? string.Empty;
            puzzles.Add(BuildPuzzle(
                property.Name,
                property.Value,
                providerMetadata.GetValueOrDefault(providerId),
                registrations,
                components.Where(component => component.PuzzleId.Equals(
                    property.Name,
                    StringComparison.OrdinalIgnoreCase)).ToArray(),
                usages.Where(usage => usage.PuzzleId.Equals(
                    property.Name,
                    StringComparison.OrdinalIgnoreCase)).Select(usage => usage.Usage).ToArray()));
        }

        foreach (var orphan in components.Where(component => !puzzles.Any(puzzle =>
            puzzle.Id.Equals(component.PuzzleId, StringComparison.OrdinalIgnoreCase))))
        {
            diagnostics.Add(Issue(
                "map.puzzle-missing",
                FusionPuzzleDiagnosticSeverity.Warning,
                orphan.PuzzleId,
                $"L'evento #{orphan.EventId} in Map {orphan.MapId:000} usa un puzzle non definito.",
                orphan.EventName,
                orphan.MapId));
        }
        diagnostics.AddRange(puzzles.SelectMany(puzzle => puzzle.Diagnostics));

        return CreateDocument(
            project,
            descriptor,
            databasePath,
            plugins,
            diagnostics,
            puzzles.OrderBy(puzzle => puzzle.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            schemaVersion,
            databaseVersion);
    }

    private static FusionPuzzleWorkspaceDocument CreateDocument(
        ZiapProject project,
        DocumentDescriptor descriptor,
        string databasePath,
        IReadOnlyList<FusionPuzzlePluginStatus> plugins,
        IReadOnlyList<FusionPuzzleDiagnostic> diagnostics,
        IReadOnlyList<FusionPuzzleDefinition> puzzles,
        int? schemaVersion,
        string? databaseVersion) => new()
    {
        Descriptor = descriptor,
        ProjectPath = project.Path,
        SourcePath = databasePath,
        SchemaVersion = schemaVersion,
        DatabaseVersion = databaseVersion,
        Plugins = plugins,
        Puzzles = puzzles,
        Diagnostics = diagnostics
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.PuzzleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray(),
    };

    private FusionPuzzleDefinition BuildPuzzle(
        string puzzleId,
        JsonElement source,
        ProviderMetadata? metadata,
        IReadOnlyList<RpgMakerPluginRegistration> registrations,
        IReadOnlyList<FusionPuzzleMapComponent> components,
        IReadOnlyList<FusionPuzzleArenaUsage> usages)
    {
        var providerId = ReadString(source, "type") ?? string.Empty;
        var diagnostics = new List<FusionPuzzleDiagnostic>();
        var roles = ReadRoles(source, components);
        foreach (var role in roles.Where(role => !role.IsSatisfied))
        {
            diagnostics.Add(Issue(
                "role.count-invalid",
                FusionPuzzleDiagnosticSeverity.Error,
                puzzleId,
                $"Il ruolo '{role.Role}' richiede {role.RequirementText}, ma ne sono stati trovati {role.Found}.",
                "I componenti vengono cercati nei note tag e nei commenti evento delle mappe."));
        }
        foreach (var role in roles.Where(role => metadata?.Roles.Count > 0 &&
            !metadata.Roles.ContainsKey(role.Role)))
        {
            diagnostics.Add(Issue(
                "provider.role-unsupported",
                FusionPuzzleDiagnosticSeverity.Error,
                puzzleId,
                $"Il provider '{providerId}' non dichiara il ruolo '{role.Role}'.",
                "Allineare la definizione del puzzle con providers.<id>.roles."));
        }
        foreach (var component in components.Where(component => !roles.Any(role =>
            role.Role.Equals(component.Role, StringComparison.OrdinalIgnoreCase))))
        {
            diagnostics.Add(Issue(
                "component.role-undeclared",
                FusionPuzzleDiagnosticSeverity.Error,
                puzzleId,
                $"L'evento #{component.EventId} usa il ruolo non dichiarato '{component.Role}'.",
                component.EventText,
                component.MapId));
        }

        var sources = ReadSources(source);
        foreach (var item in sources.Where(item => metadata?.RequiresSourceBudgets == true &&
            item.Budget is null))
        {
            diagnostics.Add(Issue(
                "source.budget-missing",
                FusionPuzzleDiagnosticSeverity.Error,
                puzzleId,
                $"La sorgente '{item.Key}' non definisce un budget.",
                $"Il provider {metadata!.DisplayName} richiede un budget per ogni sorgente."));
        }
        foreach (var item in sources.Where(item => metadata?.RequiresSourceBudgets == true &&
            !components.Any(component =>
            component.Role.Equals(metadata!.SourceRole, StringComparison.OrdinalIgnoreCase) &&
            component.Key?.Equals(item.Key, StringComparison.OrdinalIgnoreCase) == true)))
        {
            diagnostics.Add(Issue(
                "source.component-missing",
                FusionPuzzleDiagnosticSeverity.Error,
                puzzleId,
                $"Nessun evento source usa la chiave '{item.Key}'.",
                "Aggiungere il tag <FusionPuzzleKey:...> al componente source corrispondente."));
        }

        foreach (var duplicate in components
            .Where(_ => metadata?.RequiresSourceBudgets == true)
            .Where(component => component.Role.Equals(metadata!.SourceRole, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(component.Key))
            .GroupBy(component => component.Key!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add(Issue(
                "source.key-duplicate",
                FusionPuzzleDiagnosticSeverity.Error,
                puzzleId,
                $"La chiave source '{duplicate.Key}' e usata da {duplicate.Count()} eventi.",
                string.Join(", ", duplicate.Select(component => component.EventText))));
        }

        if (usages.Count == 0)
        {
            diagnostics.Add(Issue(
                "startup.not-referenced",
                FusionPuzzleDiagnosticSeverity.Warning,
                puzzleId,
                "Il puzzle non e referenziato da alcuna arena.",
                "Puo comunque essere avviato tramite FusionPuzzle.prepare o il plugin command Prepare."));
        }

        if (metadata is null)
        {
            diagnostics.Add(Issue(
                "provider.metadata-missing",
                FusionPuzzleDiagnosticSeverity.Warning,
                puzzleId,
                $"Il provider '{providerId}' non dichiara metadata nel database.",
                "Il puzzle resta leggibile, ma Studio non puo descrivere dipendenze, segnali e completamento specifici."));
        }

        var dependencies = BuildDependencies(metadata, registrations);
        foreach (var dependency in dependencies.Where(item => item.IsRequired && !item.IsAvailable))
        {
            diagnostics.Add(Issue(
                "provider.dependency-missing",
                FusionPuzzleDiagnosticSeverity.Error,
                puzzleId,
                $"La dipendenza '{dependency.Id}' non e attiva.",
                dependency.Purpose));
        }

        var calls = BuildCalls(metadata, roles, usages);
        var graph = BuildGraph(puzzleId, providerId, metadata, roles, usages, dependencies);
        return new FusionPuzzleDefinition
        {
            Id = puzzleId,
            DisplayName = ReadString(source, "displayName") ?? puzzleId,
            ProviderId = providerId,
            Scope = ReadString(source, "scope") ?? "runtime",
            BudgetPolicy = ReadString(source, "budgetPolicy") ?? "non definita",
            Repeatable = ReadBool(source, "repeatable"),
            DefinitionVersion = ReadInt(source, "definitionVersion"),
            Roles = roles,
            Sources = sources,
            Settings = ReadSettings(source),
            Components = components
                .OrderBy(component => component.MapId)
                .ThenBy(component => component.EventId)
                .ToArray(),
            ArenaUsages = usages,
            Dependencies = dependencies,
            Calls = calls,
            Diagnostics = diagnostics,
            Graph = graph,
        };
    }

    private static IReadOnlyList<FusionPuzzleRoleRequirement> ReadRoles(
        JsonElement source,
        IReadOnlyList<FusionPuzzleMapComponent> components)
    {
        if (!source.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Object)
        {
            return [];
        }
        return roles.EnumerateObject().Select(role =>
        {
            var exact = ReadInt(role.Value, "count");
            var minimum = exact ?? ReadInt(role.Value, "minimum") ??
                ReadInt(role.Value, "minCount") ?? 0;
            var maximum = exact ?? ReadInt(role.Value, "maximum") ?? ReadInt(role.Value, "maxCount");
            return new FusionPuzzleRoleRequirement
            {
                Role = role.Name,
                Minimum = minimum,
                Maximum = maximum,
                Found = components.Count(component => component.Role.Equals(
                    role.Name,
                    StringComparison.OrdinalIgnoreCase)),
            };
        }).OrderBy(role => role.Role, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static IReadOnlyList<FusionPuzzleSourceDefinition> ReadSources(JsonElement source)
    {
        if (!source.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Object)
        {
            return [];
        }
        return sources.EnumerateObject().Select(item => new FusionPuzzleSourceDefinition
        {
            Key = item.Name,
            Budget = ReadInt(item.Value, "budget"),
        }).OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static IReadOnlyList<FusionPuzzleSetting> ReadSettings(JsonElement source)
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "definitionVersion",
            "displayName",
            "type",
            "provider",
            "scope",
            "budgetPolicy",
            "repeatable",
            "roles",
            "sources",
        };
        var settings = new List<FusionPuzzleSetting>();
        foreach (var property in source.EnumerateObject().Where(property => !reserved.Contains(property.Name)))
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                settings.AddRange(property.Value.EnumerateObject().Select(value => new FusionPuzzleSetting
                {
                    Group = property.Name,
                    Name = value.Name,
                    Value = FormatValue(value.Value),
                }));
            }
            else
            {
                settings.Add(new FusionPuzzleSetting
                {
                    Group = "definition",
                    Name = property.Name,
                    Value = FormatValue(property.Value),
                });
            }
        }
        return settings;
    }

    private static IReadOnlyDictionary<string, ProviderMetadata> ReadProviderMetadata(JsonElement root)
    {
        if (!root.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, ProviderMetadata>(StringComparer.OrdinalIgnoreCase);
        }
        return providers.EnumerateObject().ToDictionary(
            provider => provider.Name,
            provider =>
            {
                var roles = new Dictionary<string, ProviderRoleMetadata>(StringComparer.OrdinalIgnoreCase);
                if (provider.Value.TryGetProperty("roles", out var roleCollection) &&
                    roleCollection.ValueKind == JsonValueKind.Object)
                {
                    foreach (var role in roleCollection.EnumerateObject())
                    {
                        roles[role.Name] = new ProviderRoleMetadata(
                            role.Name,
                            ReadString(role.Value, "displayName") ?? role.Name,
                            ReadString(role.Value, "interaction") ?? $"provider.interact('{role.Name}')");
                    }
                }
                var signals = new List<ProviderSignalMetadata>();
                if (provider.Value.TryGetProperty("signals", out var signalCollection) &&
                    signalCollection.ValueKind == JsonValueKind.Array)
                {
                    signals.AddRange(signalCollection.EnumerateArray()
                        .Where(signal => !string.IsNullOrWhiteSpace(ReadString(signal, "name")))
                        .Select(signal => new ProviderSignalMetadata(
                            ReadString(signal, "name")!,
                            ReadString(signal, "role"),
                            ReadString(signal, "phase") ?? "execution")));
                }
                var completion = provider.Value.TryGetProperty("completion", out var completionValue) &&
                    completionValue.ValueKind == JsonValueKind.Object
                        ? new ProviderCompletionMetadata(
                            ReadString(completionValue, "role"),
                            ReadString(completionValue, "signal") ?? "puzzle:completed",
                            ReadString(completionValue, "description") ?? "Il provider completa il puzzle.")
                        : null;
                return new ProviderMetadata(
                    provider.Name,
                    ReadString(provider.Value, "displayName") ?? provider.Name,
                    ReadString(provider.Value, "plugin"),
                    ReadStringArray(provider.Value, "dependencies"),
                    roles,
                    signals,
                    completion,
                    ReadBool(provider.Value, "requiresSourceBudgets"),
                    ReadString(provider.Value, "sourceRole") ?? "source");
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<FusionPuzzleDependency> BuildDependencies(
        ProviderMetadata? metadata,
        IReadOnlyList<RpgMakerPluginRegistration> registrations)
    {
        var result = new List<FusionPuzzleDependency>
        {
            Dependency(
                FusionPuzzleIntegrationProvider.CorePluginName,
                "Fusion Puzzle core",
                true,
                "Carica il database, esegue lo scanner e gestisce il lifecycle.",
                registrations),
        };
        if (!string.IsNullOrWhiteSpace(metadata?.PluginId))
        {
            result.Add(Dependency(
                metadata.PluginId,
                metadata.DisplayName,
                true,
                $"Registra il provider '{metadata.Id}' e il relativo runtime.",
                registrations));
        }
        foreach (var dependencyId in metadata?.Dependencies ?? [])
        {
            result.Add(Dependency(
                dependencyId,
                dependencyId,
                true,
                $"Dipendenza runtime dichiarata dal provider '{metadata!.Id}'.",
                registrations));
        }
        return result
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static FusionPuzzleDependency Dependency(
        string id,
        string displayName,
        bool required,
        string purpose,
        IReadOnlyList<RpgMakerPluginRegistration> registrations) => new()
    {
        Id = id,
        DisplayName = displayName,
        IsRequired = required,
        IsAvailable = IsPluginActive(registrations, id),
        Purpose = purpose,
    };

    private static IReadOnlyList<FusionPuzzleCall> BuildCalls(
        ProviderMetadata? metadata,
        IReadOnlyList<FusionPuzzleRoleRequirement> roles,
        IReadOnlyList<FusionPuzzleArenaUsage> usages)
    {
        var startupCaller = usages.Count == 0 ? "Plugin command / script" : "FusionArena";
        var calls = new List<FusionPuzzleCall>
        {
            Call(1, FusionPuzzleCallKind.Startup, "DataManager.onLoad", "$dataFusionPuzzles", "Carica FusionPuzzles.json.", "Le definizioni diventano disponibili al runtime."),
            Call(2, FusionPuzzleCallKind.Startup, startupCaller, "FusionPuzzle.prepare(id, options)", "Crea lo scanner della mappa e il contesto del puzzle.", "Istanza preparata, ma non ancora necessariamente attiva."),
            Call(3, FusionPuzzleCallKind.Validation, "FusionPuzzle.prepare", "scanner + provider.validate", "Verifica ruoli, chiavi e requisiti del provider.", "Gli errori impediscono l'attivazione."),
            Call(4, FusionPuzzleCallKind.Execution, "FusionPuzzle.prepare", "provider.prepare()", "Inizializza lo stato specifico del provider.", "Emette puzzle:prepared."),
            Call(5, FusionPuzzleCallKind.Execution, startupCaller, "FusionPuzzle.activate(id)", "Avvia il provider dopo l'avvio dell'arena.", "provider.start() ed evento puzzle:activated."),
            Call(6, FusionPuzzleCallKind.Execution, "Game_Event.start", "FusionPuzzle.interactEvent(event)", "Instrada l'interazione del giocatore al provider attivo.", "Il ruolo dell'evento decide l'operazione."),
        };
        foreach (var role in roles)
        {
            var roleMetadata = metadata?.Roles.GetValueOrDefault(role.Role);
            calls.Add(Call(
                calls.Count + 1,
                FusionPuzzleCallKind.Execution,
                $"Evento ruolo '{role.Role}'",
                roleMetadata?.Interaction ?? $"provider.interact('{role.Role}')",
                "Il provider riceve il componente e i relativi tag key/value.",
                "Lo stato risultante dipende dal contratto del provider."));
        }
        foreach (var signal in metadata?.Signals ?? [])
        {
            calls.Add(Call(
                calls.Count + 1,
                signal.Phase.Equals("completion", StringComparison.OrdinalIgnoreCase)
                    ? FusionPuzzleCallKind.Completion
                    : FusionPuzzleCallKind.Signal,
                string.IsNullOrWhiteSpace(signal.Role) ? metadata!.DisplayName : $"Ruolo '{signal.Role}'",
                signal.Name,
                "Segnale dichiarato nei metadata del provider.",
                "I binding e gli altri sistemi possono consumare il payload."));
        }
        if (metadata?.Completion is { } completion)
        {
            calls.Add(Call(
                calls.Count + 1,
                FusionPuzzleCallKind.Completion,
                string.IsNullOrWhiteSpace(completion.Role) ? metadata.DisplayName : $"Ruolo '{completion.Role}'",
                "context.complete(payload, options)",
                completion.Description,
                $"Emette {completion.Signal} e aggiorna lo stato dell'istanza."));
        }
        foreach (var binding in usages.SelectMany(usage => usage.Bindings))
        {
            calls.Add(Call(
                calls.Count + 1,
                FusionPuzzleCallKind.Signal,
                binding.Signal,
                binding.ActionsText,
                $"Binding arena '{binding.Id}'.",
                "L'azione configurata viene inoltrata all'arena/encounter."));
        }
        calls.Add(Call(
            calls.Count + 1,
            FusionPuzzleCallKind.Cleanup,
            "FusionArena / map setup",
            "FusionPuzzle.abort(id)",
            "Termina le istanze residue e ripristina le restrizioni.",
            "Emette puzzle:aborted e rimuove l'istanza."));
        return calls;
    }

    private static FusionPuzzleCall Call(
        int order,
        FusionPuzzleCallKind kind,
        string caller,
        string api,
        string description,
        string outcome) => new()
    {
        Order = order,
        Kind = kind,
        Caller = caller,
        Api = api,
        Description = description,
        Outcome = outcome,
    };

    private static FusionPuzzleGraph BuildGraph(
        string puzzleId,
        string providerId,
        ProviderMetadata? metadata,
        IReadOnlyList<FusionPuzzleRoleRequirement> roles,
        IReadOnlyList<FusionPuzzleArenaUsage> usages,
        IReadOnlyList<FusionPuzzleDependency> dependencies)
    {
        var nodes = new List<FusionPuzzleGraphNode>();
        var edges = new List<FusionPuzzleGraphEdge>();
        AddNode(nodes, "database", "Definizione puzzle", puzzleId, FusionPuzzleGraphNodeKind.Database, 0, "DATA", "Record letto da FusionPuzzles.json.", "DataManager.onLoad", "Definizione registrata.");
        AddNode(nodes, "startup", usages.Count == 0 ? "Avvio esplicito" : "Avvio da Fusion Arena", usages.Count == 0 ? "FusionPuzzle.prepare" : string.Join(", ", usages.Select(usage => usage.ArenaId)), FusionPuzzleGraphNodeKind.Startup, 1, "START", usages.Count == 0 ? "Nessuna arena referenzia il puzzle." : "L'arena materializza il provider come dipendenza.", "prepare(id, { activate:false })", "Istanza creata.", usages.Count == 0);
        AddNode(nodes, "scan", "Scanner componenti", "FusionPuzzleMapScanner", FusionPuzzleGraphNodeKind.Validation, 2, "SCAN", "Legge note e commenti di tutti gli eventi mappa.", "scanMap()", "Ruoli e metadati associati agli eventi.");
        AddNode(nodes, "validate", "Validazione", "provider.validate", FusionPuzzleGraphNodeKind.Validation, 3, "CHECK", "Confronta i componenti con i requisiti della definizione.", "validate(context)", "Preparazione consentita o bloccata.", roles.Any(role => !role.IsSatisfied));
        var providerMissing = dependencies.Any(item => item.IsRequired && !item.IsAvailable);
        AddNode(nodes, "provider", metadata?.DisplayName ?? (string.IsNullOrWhiteSpace(providerId) ? "Provider non dichiarato" : providerId), $"registerProvider('{providerId}')", providerMissing ? FusionPuzzleGraphNodeKind.Missing : FusionPuzzleGraphNodeKind.Provider, 4, "PROVIDER", "Implementa prepare, start, interact, snapshot e abort.", "provider.prepare() / provider.start()", "Puzzle attivo.", providerMissing);
        AddNode(nodes, "active", "Istanza attiva", "puzzle:activated", FusionPuzzleGraphNodeKind.State, 5, "ACTIVE", "Riceve le interazioni degli eventi associati.", "FusionPuzzle.interactEvent", "Il provider aggiorna il proprio stato.");
        Edge(edges, "database", "startup", "id", "L'host risolve il record del puzzle.");
        Edge(edges, "startup", "scan", "prepare", "La preparazione crea lo scanner della mappa corrente.");
        Edge(edges, "scan", "validate", "componenti", "Ruoli, chiavi e valori vengono validati.");
        Edge(edges, "validate", "provider", "factory", "Il core risolve il provider registrato per type.");
        Edge(edges, "provider", "active", "activate", "start() porta l'istanza nello stato active.");

        foreach (var role in roles)
        {
            var roleId = $"role-{NormalizeId(role.Role)}";
            var roleMetadata = metadata?.Roles.GetValueOrDefault(role.Role);
            AddNode(nodes, roleId, roleMetadata?.DisplayName ?? $"Ruolo: {role.Role}", role.Role, FusionPuzzleGraphNodeKind.Component, 6, role.FoundText, "Game_Event.start inoltra l'evento e i relativi tag.", roleMetadata?.Interaction ?? $"provider.interact('{role.Role}')", role.IsSatisfied ? "Interazione disponibile." : "Requisito non soddisfatto.", !role.IsSatisfied);
            Edge(edges, "active", roleId, "interact", $"Evento con <FusionPuzzleRole:{role.Role}>.");
        }

        var signalNodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var signalIndex = 0;
        foreach (var signal in metadata?.Signals ?? [])
        {
            var signalId = $"signal-{signalIndex++}";
            signalNodes[signal.Name] = signalId;
            AddNode(nodes, signalId, signal.Name, signal.Phase, FusionPuzzleGraphNodeKind.Signal, 7, "SIGNAL", "Segnale pubblicato dal provider.", $"context.signal('{signal.Name}')", "I consumer registrati ricevono il payload.");
            var sourceId = string.IsNullOrWhiteSpace(signal.Role)
                ? "active"
                : $"role-{NormalizeId(signal.Role)}";
            if (!nodes.Any(node => node.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase)))
            {
                sourceId = "active";
            }
            Edge(edges, sourceId, signalId, "emit", "Il provider pubblica il segnale dichiarato.");
        }

        var completion = metadata?.Completion;
        var completionSignal = completion?.Signal ?? "puzzle:completed";
        AddNode(nodes, "completed", "Completamento", completionSignal, FusionPuzzleGraphNodeKind.Completion, 8, "DONE", completion?.Description ?? "Il provider dichiara concluso il puzzle.", "context.complete(payload, options)", "L'host riceve il segnale finale.");
        if (signalNodes.TryGetValue(completionSignal, out var completionSignalId))
        {
            Edge(edges, completionSignalId, "completed", "complete", "Il segnale chiude il lifecycle dichiarato.");
        }
        else
        {
            var completionSourceId = string.IsNullOrWhiteSpace(completion?.Role)
                ? "active"
                : $"role-{NormalizeId(completion.Role)}";
            if (!nodes.Any(node => node.Id.Equals(completionSourceId, StringComparison.OrdinalIgnoreCase)))
            {
                completionSourceId = "active";
            }
            Edge(edges, completionSourceId, "completed", "complete", completion?.Description ?? "Percorso di completamento definito dal provider.");
        }

        var bindingIndex = 0;
        foreach (var binding in usages.SelectMany(usage => usage.Bindings))
        {
            if (!signalNodes.TryGetValue(binding.Signal, out var signalId))
            {
                signalId = $"binding-signal-{bindingIndex++}";
                signalNodes[binding.Signal] = signalId;
                AddNode(nodes, signalId, binding.Signal, binding.Id, FusionPuzzleGraphNodeKind.Signal, 7, "SIGNAL", $"Segnale consumato dall'arena {binding.ArenaId}.", binding.Signal, binding.ActionsText);
                Edge(edges, "provider", signalId, "emit", "Il provider pubblica il segnale usato dal binding.");
            }
            foreach (var action in binding.Actions)
            {
                var actionId = $"action-{bindingIndex++}";
                AddNode(nodes, actionId, action, action, FusionPuzzleGraphNodeKind.Signal, 9, "CALL", $"Consumer configurato dal binding per {binding.ProviderInstanceId}.", action, "L'arena o l'encounter prosegue.");
                Edge(edges, signalId, actionId, "dispatch", "Il binding converte il segnale in una chiamata host.");
            }
        }
        AddNode(nodes, "cleanup", "Cleanup", "FusionPuzzle.abort", FusionPuzzleGraphNodeKind.Cleanup, 10, "END", "Rilascia le risorse possedute dall'istanza runtime.", "provider.abort()", "Stato ripristinato e istanza rimossa.");
        Edge(edges, "completed", "cleanup", "release", "La chiusura arena elimina eventuali istanze residue.");
        return new FusionPuzzleGraph
        {
            Nodes = nodes,
            Edges = edges,
            Summary = $"{nodes.Count} nodi, {edges.Count} collegamenti: bootstrap, componenti, segnali e cleanup.",
        };
    }

    private static void AddNode(
        ICollection<FusionPuzzleGraphNode> nodes,
        string id,
        string displayName,
        string technicalId,
        FusionPuzzleGraphNodeKind kind,
        int level,
        string badge,
        string description,
        string execution,
        string outcome,
        bool warning = false) => nodes.Add(new FusionPuzzleGraphNode
    {
        Id = id,
        DisplayName = displayName,
        TechnicalId = technicalId,
        Kind = kind,
        Level = level,
        BadgeText = badge,
        Description = description,
        Execution = execution,
        Outcome = outcome,
        IsWarning = warning,
    });

    private static void Edge(
        ICollection<FusionPuzzleGraphEdge> edges,
        string source,
        string target,
        string label,
        string detail) => edges.Add(new FusionPuzzleGraphEdge
    {
        SourceNodeId = source,
        TargetNodeId = target,
        Label = label,
        Detail = detail,
    });

    private async Task<IReadOnlyDictionary<int, string>> LoadMapNamesAsync(
        ZiapProject project,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(project.Path, "data", "MapInfos.json");
        if (!_fileSystem.FileExists(path))
        {
            return new Dictionary<int, string>();
        }
        using var document = JsonDocument.Parse(await _fileSystem.ReadAllTextAsync(path, cancellationToken));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<int, string>();
        }
        return document.RootElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object && ReadInt(item, "id") is > 0)
            .ToDictionary(item => ReadInt(item, "id")!.Value, item => ReadString(item, "name") ?? "Senza nome");
    }

    private async Task<IReadOnlyList<FusionPuzzleMapComponent>> ScanMapComponentsAsync(
        ZiapProject project,
        IReadOnlyDictionary<int, string> mapNames,
        ICollection<FusionPuzzleDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var dataPath = Path.Combine(project.Path, "data");
        if (!_fileSystem.DirectoryExists(dataPath))
        {
            return [];
        }
        var result = new List<FusionPuzzleMapComponent>();
        foreach (var path in _fileSystem.EnumerateFiles(dataPath, "Map*.json")
            .Where(path => MapFileRegex().IsMatch(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var match = MapFileRegex().Match(Path.GetFileName(path));
            var mapId = int.Parse(match.Groups[1].Value);
            try
            {
                using var document = JsonDocument.Parse(await _fileSystem.ReadAllTextAsync(path, cancellationToken));
                if (!document.RootElement.TryGetProperty("events", out var events) ||
                    events.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (var gameEvent in events.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
                {
                    var source = BuildEventTagSource(gameEvent);
                    var puzzleIds = PuzzleIdRegex().Matches(source).Select(item => item.Groups[1].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    var role = FirstTagValue(RoleRegex(), source);
                    if (puzzleIds.Length == 0 || string.IsNullOrWhiteSpace(role))
                    {
                        continue;
                    }
                    foreach (var puzzleId in puzzleIds)
                    {
                        result.Add(new FusionPuzzleMapComponent
                        {
                            PuzzleId = puzzleId,
                            MapId = mapId,
                            MapName = mapNames.GetValueOrDefault(mapId, "Senza nome"),
                            EventId = ReadInt(gameEvent, "id") ?? 0,
                            EventName = ReadString(gameEvent, "name") ?? "Senza nome",
                            Role = role,
                            Key = FirstTagValue(KeyRegex(), source),
                            Value = FirstTagValue(ValueRegex(), source),
                            Source = ReadString(gameEvent, "note")?.Contains("FusionPuzzle", StringComparison.OrdinalIgnoreCase) == true ? "Note evento" : "Commento evento",
                        });
                    }
                }
            }
            catch (JsonException exception)
            {
                diagnostics.Add(Issue(
                    "map.json-invalid",
                    FusionPuzzleDiagnosticSeverity.Warning,
                    string.Empty,
                    $"Map {mapId:000} non puo essere analizzata.",
                    exception.Message,
                    mapId));
            }
        }
        return result;
    }

    private static string BuildEventTagSource(JsonElement gameEvent)
    {
        var lines = new List<string>();
        var note = ReadString(gameEvent, "note");
        if (!string.IsNullOrWhiteSpace(note))
        {
            lines.Add(note);
        }
        if (!gameEvent.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
        {
            return string.Join('\n', lines);
        }
        foreach (var page in pages.EnumerateArray())
        {
            if (!page.TryGetProperty("list", out var commands) || commands.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var command in commands.EnumerateArray())
            {
                var code = ReadInt(command, "code");
                if (code is not (108 or 408) ||
                    !command.TryGetProperty("parameters", out var parameters) ||
                    parameters.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                lines.AddRange(parameters.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty));
            }
        }
        return string.Join('\n', lines);
    }

    private async Task<IReadOnlyList<ParsedArenaUsage>> LoadArenaUsagesAsync(
        ZiapProject project,
        ICollection<FusionPuzzleDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(project.Path, "data", "FusionArenas.json");
        if (!_fileSystem.FileExists(path))
        {
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(await _fileSystem.ReadAllTextAsync(path, cancellationToken));
            if (!document.RootElement.TryGetProperty("arenas", out var arenas) || arenas.ValueKind != JsonValueKind.Object)
            {
                return [];
            }
            var result = new List<ParsedArenaUsage>();
            foreach (var arena in arenas.EnumerateObject())
            {
                var mapIds = ReadIntegerArray(arena.Value, "mapIds");
                var bindings = ReadBindings(arena.Name, arena.Value);
                if (!arena.Value.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (var provider in providers.EnumerateArray().Where(item =>
                    ReadString(item, "type")?.Equals("fusionPuzzle", StringComparison.OrdinalIgnoreCase) == true))
                {
                    var instanceId = ReadString(provider, "instanceId") ?? string.Empty;
                    var puzzleId = ReadString(provider, "puzzleId") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(puzzleId))
                    {
                        continue;
                    }
                    result.Add(new ParsedArenaUsage(puzzleId, new FusionPuzzleArenaUsage
                    {
                        ArenaId = arena.Name,
                        DisplayName = ReadString(arena.Value, "displayName") ?? arena.Name,
                        ProviderInstanceId = instanceId,
                        Scope = ReadString(provider, "scope") ?? "arena",
                        IsRequired = ReadBool(provider, "required"),
                        MapIds = mapIds,
                        Bindings = bindings.Where(binding => binding.ProviderInstanceId.Equals(
                            instanceId,
                            StringComparison.OrdinalIgnoreCase)).ToArray(),
                    }));
                }
            }
            return result;
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Issue(
                "arena.database-invalid",
                FusionPuzzleDiagnosticSeverity.Warning,
                string.Empty,
                "FusionArenas.json non puo essere analizzato.",
                exception.Message));
            return [];
        }
    }

    private static IReadOnlyList<FusionPuzzleBinding> ReadBindings(string arenaId, JsonElement arena)
    {
        if (!arena.TryGetProperty("puzzleBindings", out var bindings) || bindings.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return bindings.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new FusionPuzzleBinding
            {
                Id = ReadString(item, "id") ?? "binding",
                ArenaId = arenaId,
                ProviderInstanceId = ReadString(item, "providerInstanceId") ?? string.Empty,
                Signal = ReadString(item, "signal") ?? string.Empty,
                Actions = ReadStringArray(item, "action"),
            }).ToArray();
    }

    private static IReadOnlyList<int> ReadIntegerArray(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return value.EnumerateArray().Where(item => item.TryGetInt32(out _)).Select(item => item.GetInt32()).ToArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value))
        {
            return [];
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            return [value.GetString() ?? string.Empty];
        }
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? string.Empty).ToArray()
            : [];
    }

    private static bool IsPluginActive(
        IReadOnlyList<RpgMakerPluginRegistration> registrations,
        string pluginName) => registrations.Any(plugin => plugin.IsActive &&
            RpgMakerPluginRegistryService.PluginNameEquals(plugin.Name, pluginName));

    private static string? ReadString(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? ReadInt(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) &&
        value.TryGetInt32(out var result) ? result : null;

    private static bool ReadBool(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static string FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => value.GetRawText(),
    };

    private static string? FirstTagValue(Regex regex, string source)
    {
        var match = regex.Match(source);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string NormalizeId(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    private static FusionPuzzleDiagnostic Issue(
        string code,
        FusionPuzzleDiagnosticSeverity severity,
        string puzzleId,
        string message,
        string detail,
        int? mapId = null) => new()
    {
        Code = code,
        Severity = severity,
        PuzzleId = puzzleId,
        MapId = mapId,
        Message = message,
        Detail = detail,
    };

    [GeneratedRegex(@"^Map(\d{3})\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex MapFileRegex();

    [GeneratedRegex(@"<FusionPuzzle\s*:\s*([^>]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex PuzzleIdRegex();

    [GeneratedRegex(@"<FusionPuzzleRole\s*:\s*([^>]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex RoleRegex();

    [GeneratedRegex(@"<FusionPuzzleKey\s*:\s*([^>]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"<FusionPuzzleValue\s*:\s*([^>]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex ValueRegex();

    private sealed record PluginSpec(string Id, string DisplayName, bool IsRequired);
    private sealed record ParsedArenaUsage(string PuzzleId, FusionPuzzleArenaUsage Usage);
    private sealed record ProviderMetadata(
        string Id,
        string DisplayName,
        string? PluginId,
        IReadOnlyList<string> Dependencies,
        IReadOnlyDictionary<string, ProviderRoleMetadata> Roles,
        IReadOnlyList<ProviderSignalMetadata> Signals,
        ProviderCompletionMetadata? Completion,
        bool RequiresSourceBudgets,
        string SourceRole);
    private sealed record ProviderRoleMetadata(
        string Id,
        string DisplayName,
        string Interaction);
    private sealed record ProviderSignalMetadata(
        string Name,
        string? Role,
        string Phase);
    private sealed record ProviderCompletionMetadata(
        string? Role,
        string Signal,
        string Description);
}
