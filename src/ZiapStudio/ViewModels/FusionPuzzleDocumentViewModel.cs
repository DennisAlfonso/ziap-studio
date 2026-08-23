using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Puzzles;

namespace ZiapStudio.ViewModels;

public sealed class FusionPuzzleDocumentViewModel : INotifyPropertyChanged
{
    private FusionPuzzleDefinition? _selectedPuzzle;
    private FusionPuzzleGraphNode? _selectedGraphNode;

    public FusionPuzzleDocumentViewModel(FusionPuzzleWorkspaceDocument document)
    {
        Document = document;
        SelectedPuzzle = Puzzles.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FusionPuzzleWorkspaceDocument Document { get; }
    public IReadOnlyList<FusionPuzzleDefinition> Puzzles => Document.Puzzles;
    public IReadOnlyList<FusionPuzzlePluginStatus> Plugins => Document.Plugins;
    public string SourcePath => Document.SourcePath;
    public string VersionText => $"Schema {Document.SchemaVersion?.ToString() ?? "?"} | DB {Document.DatabaseVersion ?? "?"}";
    public string WorkspaceStatusText => Document.IsValid
        ? $"{Puzzles.Count} puzzle letti"
        : $"{Document.ErrorCount} errori | {Document.WarningCount} avvisi";
    public string PuzzleCountText => Puzzles.Count == 1 ? "1 definizione" : $"{Puzzles.Count} definizioni";

    public FusionPuzzleDefinition? SelectedPuzzle
    {
        get => _selectedPuzzle;
        set
        {
            if (ReferenceEquals(_selectedPuzzle, value))
            {
                return;
            }
            _selectedPuzzle = value;
            _selectedGraphNode = value?.Graph.Nodes.FirstOrDefault();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedGraphNode));
            NotifySelectionChanged();
        }
    }

    public FusionPuzzleGraphNode? SelectedGraphNode
    {
        get => _selectedGraphNode;
        set
        {
            if (ReferenceEquals(_selectedGraphNode, value))
            {
                return;
            }
            _selectedGraphNode = value;
            OnPropertyChanged();
        }
    }

    public FusionPuzzleGraph Graph => SelectedPuzzle?.Graph ?? new();
    public IReadOnlyList<FusionPuzzleRoleRequirement> Roles => SelectedPuzzle?.Roles ?? [];
    public IReadOnlyList<FusionPuzzleSourceDefinition> Sources => SelectedPuzzle?.Sources ?? [];
    public IReadOnlyList<FusionPuzzleMapComponent> Components => SelectedPuzzle?.Components ?? [];
    public IReadOnlyList<FusionPuzzleArenaUsage> ArenaUsages => SelectedPuzzle?.ArenaUsages ?? [];
    public IReadOnlyList<FusionPuzzleDependency> Dependencies => SelectedPuzzle?.Dependencies ?? [];
    public IReadOnlyList<FusionPuzzleCall> Calls => SelectedPuzzle?.Calls ?? [];
    public IReadOnlyList<FusionPuzzleSetting> Settings => SelectedPuzzle?.Settings ?? [];
    public IReadOnlyList<FusionPuzzleDiagnostic> Diagnostics => Document.Diagnostics
        .Where(issue => string.IsNullOrWhiteSpace(issue.PuzzleId) ||
            issue.PuzzleId.Equals(SelectedPuzzle?.Id, StringComparison.OrdinalIgnoreCase))
        .ToArray();
    public string SelectionStatusText => SelectedPuzzle is null
        ? "Nessun puzzle disponibile"
        : $"{SelectedPuzzle.StatusText} | {SelectedPuzzle.Scope} | " +
            (SelectedPuzzle.Repeatable ? "ripetibile" : "singolo uso");
    public string ComponentCountText => Components.Count == 1 ? "1 componente mappa" : $"{Components.Count} componenti mappa";
    public string CallCountText => Calls.Count == 1 ? "1 chiamata" : $"{Calls.Count} chiamate";
    public string DiagnosticsText => Diagnostics.Count == 0
        ? "Nessuna anomalia rilevata."
        : $"{Diagnostics.Count} elementi diagnostici per la selezione.";

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(Graph));
        OnPropertyChanged(nameof(Roles));
        OnPropertyChanged(nameof(Sources));
        OnPropertyChanged(nameof(Components));
        OnPropertyChanged(nameof(ArenaUsages));
        OnPropertyChanged(nameof(Dependencies));
        OnPropertyChanged(nameof(Calls));
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(SelectionStatusText));
        OnPropertyChanged(nameof(ComponentCountText));
        OnPropertyChanged(nameof(CallCountText));
        OnPropertyChanged(nameof(DiagnosticsText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
