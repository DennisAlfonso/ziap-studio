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
        var puzzles = new List<FusionPuzzleDefinition>();
        foreach (var property in puzzleCollection.EnumerateObject())
        {
            puzzles.Add(BuildPuzzle(
                property.Name,
                property.Value,
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

        var sources = ReadSources(source);
        foreach (var item in sources.Where(item => item.Budget is null))
        {
            diagnostics.Add(Issue(
                "source.budget-missing",
                FusionPuzzleDiagnosticSeverity.Error,
                puzzleId,
                $"La sorgente '{item.Key}' non definisce un budget.",
                "Il provider Hexella Weight usa questo valore per inizializzare i passi disponibili."));
        }
        foreach (var item in sources.Where(item => !components.Any(component =>
            component.Role.Equals("source", StringComparison.OrdinalIgnoreCase) &&
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
            .Where(component => component.Role.Equals("source", StringComparison.OrdinalIgnoreCase) &&
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

        var dependencies = BuildDependencies(providerId, registrations);
        foreach (var dependency in dependencies.Where(item => item.IsRequired && !item.IsAvailable))
        {
            diagnostics.Add(Issue(
                "provider.dependency-missing",
                FusionPuzzleDiagnosticSeverity.Error,
                puzzleId,
                $"La dipendenza '{dependency.Id}' non e attiva.",
                dependency.Purpose));
        }

        var calls = BuildCalls(providerId, usages);
        var graph = BuildGraph(puzzleId, providerId, roles, usages, dependencies);
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
        var settings = new List<FusionPuzzleSetting>();
        foreach (var groupName in new[] { "carry", "legacyBridge" })
        {
            if (!source.TryGetProperty(groupName, out var group) || group.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            settings.AddRange(group.EnumerateObject().Select(property => new FusionPuzzleSetting
            {
                Group = groupName,
                Name = property.Name,
                Value = FormatValue(property.Value),
            }));
        }
        return settings;
    }

    private static IReadOnlyList<FusionPuzzleDependency> BuildDependencies(
        string providerId,
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
        if (providerId.Equals("hexellaWeight", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(Dependency(
                FusionPuzzleIntegrationProvider.HexellaWeightPluginName,
                "Provider Hexella Weight",
                true,
                "Registra il provider, le interazioni e i segnali di consegna.",
                registrations));
            result.Add(Dependency(
                FusionPuzzleIntegrationProvider.MovementPluginName,
                "Movement bridge",
                true,
                "Conta i passi e applica i lock di dash e salto.",
                registrations));
        }
        else
        {
            result.Add(new FusionPuzzleDependency
            {
                Id = $"provider:{providerId}",
                DisplayName = string.IsNullOrWhiteSpace(providerId) ? "Provider non dichiarato" : providerId,
                IsRequired = true,
                IsAvailable = false,
                Purpose = "Il provider custom non puo essere associato automaticamente a un plugin registrato.",
            });
        }
        return result;
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
        string providerId,
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
        if (providerId.Equals("hexellaWeight", StringComparison.OrdinalIgnoreCase))
        {
            calls.Add(Call(7, FusionPuzzleCallKind.Execution, "Game_Player.increaseSteps", "FusionPuzzleMovement.emitPlayerStep", "Notifica ogni passo mentre il peso e trasportato.", "Il budget residuo viene decrementato o il trasporto scade."));
            calls.Add(Call(8, FusionPuzzleCallKind.Signal, "HexellaWeight provider", "puzzle:carryUpdated", "Pubblica lo stato di trasporto dopo source, extension, jump e step.", "HUD e bridge legacy possono sincronizzarsi."));
            calls.Add(Call(9, FusionPuzzleCallKind.Completion, "goal.interact", "puzzle:deliveryCompleted", "Consegna il peso al componente goal.", "Emette puzzle:completed e rilascia i lock."));
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
        AddNode(nodes, "provider", string.IsNullOrWhiteSpace(providerId) ? "Provider mancante" : providerId, $"registerProvider('{providerId}')", providerMissing ? FusionPuzzleGraphNodeKind.Missing : FusionPuzzleGraphNodeKind.Provider, 4, "PROVIDER", "Implementa prepare, start, interact e abort.", "provider.prepare() / provider.start()", "Puzzle attivo.", providerMissing);
        AddNode(nodes, "active", "Istanza attiva", "puzzle:activated", FusionPuzzleGraphNodeKind.State, 5, "ACTIVE", "Riceve le interazioni degli eventi associati.", "FusionPuzzle.interactEvent", "Il provider aggiorna il proprio stato.");
        Edge(edges, "database", "startup", "id", "L'host risolve il record del puzzle.");
        Edge(edges, "startup", "scan", "prepare", "La preparazione crea lo scanner della mappa corrente.");
        Edge(edges, "scan", "validate", "componenti", "Ruoli, chiavi e valori vengono validati.");
        Edge(edges, "validate", "provider", "factory", "Il core risolve il provider registrato per type.");
        Edge(edges, "provider", "active", "activate", "start() porta l'istanza nello stato active.");

        foreach (var role in roles)
        {
            var roleId = $"role-{NormalizeId(role.Role)}";
            AddNode(nodes, roleId, $"Ruolo: {role.Role}", role.RequirementText, FusionPuzzleGraphNodeKind.Component, 6, role.FoundText, "Game_Event.start inoltra l'evento e i relativi tag.", $"provider.interact('{role.Role}')", role.IsSatisfied ? "Interazione disponibile." : "Requisito non soddisfatto.", !role.IsSatisfied);
            Edge(edges, "active", roleId, "interact", $"Evento con <FusionPuzzleRole:{role.Role}>.");
        }

        var goal = nodes.FirstOrDefault(node => node.Id == "role-goal");
        AddNode(nodes, "completed", "Completamento", "puzzle:completed", FusionPuzzleGraphNodeKind.Completion, 8, "DONE", "Il provider dichiara concluso il puzzle.", "provider completion", "L'host riceve i segnali finali.");
        if (goal is not null)
        {
            Edge(edges, goal.Id, "completed", "complete", "L'interazione goal completa la consegna.");
        }
        else
        {
            Edge(edges, "active", "completed", "provider", "Percorso di completamento definito dal provider.");
        }

        var bindingIndex = 0;
        foreach (var binding in usages.SelectMany(usage => usage.Bindings))
        {
            var signalId = $"signal-{bindingIndex++}";
            AddNode(nodes, signalId, binding.Signal, binding.Id, FusionPuzzleGraphNodeKind.Signal, 9, "SIGNAL", $"Binding dichiarato nell'arena {binding.ArenaId}.", binding.Signal, binding.ActionsText);
            Edge(edges, "completed", signalId, "emit", "Il segnale viene pubblicato sul bus Fusion Puzzle.");
            foreach (var action in binding.Actions)
            {
                var actionId = $"action-{bindingIndex++}";
                AddNode(nodes, actionId, action, action, FusionPuzzleGraphNodeKind.Signal, 10, "CALL", $"Consumer configurato dal binding per {binding.ProviderInstanceId}.", action, "L'arena o l'encounter prosegue.");
                Edge(edges, signalId, actionId, "dispatch", "Il binding converte il segnale in una chiamata host.");
            }
        }
        AddNode(nodes, "cleanup", "Cleanup", "FusionPuzzle.abort", FusionPuzzleGraphNodeKind.Cleanup, bindingIndex == 0 ? 9 : 11, "END", "Rilascia lock, self-switch e istanza runtime.", "provider.abort()", "Stato ripristinato e istanza rimossa.");
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
}
