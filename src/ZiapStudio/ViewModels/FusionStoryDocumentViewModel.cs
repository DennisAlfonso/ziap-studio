using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Integration.Remote;
using ZiapStudio.Services.Localization;
using ZiapStudio.Services.Fusion.Story;

namespace ZiapStudio.ViewModels;

public sealed class FusionStoryDocumentViewModel : INotifyPropertyChanged, IDisposable
{
    private StoryNavigationItem? _selectedSource;
    private StoryBlock? _selectedBlock;
    private IReadOnlyList<StoryBlock> _blocks = [];
    private readonly ZiapProject _project;
    private readonly StoryLocalizationAuthoringService _authoringService;
    private readonly Func<Task<FusionStoryWorkspaceDocument>> _reloadWorkspace;
    private StoryLocalizationAuthoringSession? _authoringSession;
    private CancellationTokenSource? _lockHeartbeatCancellation;
    private bool _isAuthoringBusy;
    private string _authoringMessage = "Seleziona una reference master per modificare soltanto il leaf localizzato.";

    public FusionStoryDocumentViewModel(
        FusionStoryWorkspaceDocument document,
        ZiapProject project,
        StoryLocalizationAuthoringService authoringService,
        StoryCompositionService compositionService,
        Func<Task<FusionStoryWorkspaceDocument>> reloadWorkspace)
    {
        Document = document;
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _authoringService = authoringService ?? throw new ArgumentNullException(nameof(authoringService));
        _reloadWorkspace = reloadWorkspace ?? throw new ArgumentNullException(nameof(reloadWorkspace));
        Composer = new StoryComposerViewModel(
            _project,
            compositionService,
            ReloadWorkspaceAsync,
            () => _authoringSession?.Lock.AcquiredAt);
        Navigator = new StoryNavigatorViewModel(Document.Workspace);
        Navigator.LocationSelected += Navigator_LocationSelected;
        if (Navigator.SelectedPage is { } initialPage)
        {
            NavigateToLocation(initialPage.Location);
        }
        _ = Composer.LoadRecoveriesAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FusionStoryWorkspaceDocument Document { get; private set; }

    public StoryComposerViewModel Composer { get; }

    public StoryNavigatorViewModel Navigator { get; }

    public StoryNavigationItem? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (value is null || !value.IsSelectable || ReferenceEquals(_selectedSource, value))
            {
                return;
            }
            _selectedSource = value;
            _blocks = value.Blocks.Select(ProjectBlockForAuthoring).ToArray();
            SelectedBlock = _blocks.FirstOrDefault();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Blocks));
            OnPropertyChanged(nameof(SelectedSourceTitle));
            OnPropertyChanged(nameof(SelectedSourceDetail));
            Composer.SetAnchor(value.Target, SelectedBlock);
        }
    }

    public IReadOnlyList<StoryBlock> Blocks => _blocks;

    public bool IsAuthoringBusy
    {
        get => _isAuthoringBusy;
        private set => SetProperty(ref _isAuthoringBusy, value);
    }

    public bool HasAuthoringSession => _authoringSession?.EditSession is not null;

    public bool CanBeginEditing => !IsAuthoringBusy && SelectedBlock?.LocalizationOrigins.Any(IsMasterEditable) == true &&
        (_authoringSession is null || !_authoringSession.EditSession!.IsDirty ||
         SelectedBlock.LocalizationOrigins.Any(origin => IsSameFile(origin, _authoringSession.Origin)));

    public bool CanSaveToStaging => !IsAuthoringBusy && _authoringSession?.EditSession?.IsDirty == true &&
        _authoringSession.State is not LocalizationAuthoringState.Conflict and
            not LocalizationAuthoringState.LockedByOther and not LocalizationAuthoringState.LocalOutOfSync;

    public bool CanUndoLocalization => !IsAuthoringBusy && _authoringSession?.EditSession?.CanUndo == true;

    public bool CanRedoLocalization => !IsAuthoringBusy && _authoringSession?.EditSession?.CanRedo == true;

    public bool CanDiscardLocalization => !IsAuthoringBusy && _authoringSession?.EditSession?.IsDirty == true;

    public bool CanRetryMirror => !IsAuthoringBusy &&
        _authoringSession?.State == LocalizationAuthoringState.LocalOutOfSync;

    public string AuthoringStateText => _authoringSession is null
        ? "IT MASTER · READ ONLY"
        : _authoringSession.State switch
        {
            LocalizationAuthoringState.Clean => _authoringSession.EditSession!.Snapshot.HasStaging
                ? "IT MASTER · STAGING" : "IT MASTER · PUBLISHED",
            LocalizationAuthoringState.LocalChanges => "IT MASTER · LOCAL CHANGES",
            LocalizationAuthoringState.Staging => "IT MASTER · STAGING",
            LocalizationAuthoringState.Conflict => "IT MASTER · CONFLICT",
            LocalizationAuthoringState.LocalOutOfSync => "IT MASTER · LOCAL OUT OF SYNC",
            LocalizationAuthoringState.LockedByOther => "IT MASTER · LOCKED BY OTHER USER",
            _ => "IT MASTER",
        };

    public string AuthoringMessage => _authoringMessage;

    public IReadOnlyList<StoryLocalizationFieldViewModel> LocalizationFields =>
        SelectedBlock is null || _authoringSession?.EditSession is null ? [] :
        SelectedBlock.LocalizationOrigins
            .Where(origin => IsSameFile(origin, _authoringSession.Origin) &&
                _authoringSession.EditSession.CanEdit(origin))
            .Select((origin, index) => new StoryLocalizationFieldViewModel(
                GetFieldLabel(SelectedBlock, index, origin),
                origin,
                _authoringSession.EditSession.TryGetValue(origin, out var value) ? value : string.Empty,
                SetLocalizationFieldValue,
                _authoringSession.State is LocalizationAuthoringState.Conflict or
                    LocalizationAuthoringState.LockedByOther or
                    LocalizationAuthoringState.LocalOutOfSync))
            .ToArray();

    public StoryBlock? SelectedBlock
    {
        get => _selectedBlock;
        set
        {
            if (ReferenceEquals(_selectedBlock, value))
            {
                return;
            }
            _selectedBlock = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedBlockDetails));
            OnPropertyChanged(nameof(SelectedBlockRawCommands));
            NotifyAuthoringPropertiesChanged();
            Composer.SetAnchor(SelectedSource?.Target, value);
        }
    }

    public IReadOnlyList<string> SelectedBlockDetails => SelectedBlock?.Details ?? [];

    public IReadOnlyList<StoryRawCommand> SelectedBlockRawCommands => SelectedBlock?.SourceCommands ?? [];

    public string SelectedSourceTitle => SelectedSource?.Title ?? "Seleziona una page o un Common Event";

    public string SelectedSourceDetail => SelectedSource?.Context ?? "La timeline è read-only.";

    public string WorkspaceStatusText => Document.ErrorCount > 0
        ? $"{Document.ErrorCount} errori · {Document.WarningCount} avvisi"
        : $"{Document.PageCount} page · {Document.BlockCount} blocchi · authoring localizzazione";


    public async Task BeginEditingAsync()
    {
        var origin = SelectedBlock?.LocalizationOrigins.FirstOrDefault(IsMasterEditable);
        if (origin is null)
        {
            _authoringMessage = "Il blocco non contiene una reference Localization master modificabile.";
            NotifyAuthoringPropertiesChanged();
            return;
        }
        if (_authoringSession?.EditSession?.IsDirty == true && !IsSameFile(origin, _authoringSession.Origin))
        {
            _authoringMessage = "Salva o scarta le modifiche locali prima di passare a un altro file Localization.";
            NotifyAuthoringPropertiesChanged();
            return;
        }
        if (_authoringSession?.EditSession is not null && IsSameFile(origin, _authoringSession.Origin))
        {
            _authoringMessage = "Editing master già attivo per questo file Localization.";
            NotifyAuthoringPropertiesChanged();
            return;
        }

        IsAuthoringBusy = true;
        try
        {
            if (_authoringSession is not null)
            {
                await ReleaseAsync();
            }
            _authoringSession = await _authoringService.BeginAsync(_project, origin);
            if (_authoringSession.EditSession is null)
            {
                _authoringMessage = $"In modifica da {_authoringSession.Lock.Owner ?? "un altro utente"}.";
                return;
            }
            _authoringSession.EditSession.PropertyChanged += EditSession_PropertyChanged;
            StartLockHeartbeat();
            _authoringMessage = "Editing · lock acquired. Le modifiche restano locali finché non salvi nello staging.";
            RefreshStoryProjection();
        }
        catch (RemoteLocalizationAuthoringException exception)
        {
            _authoringMessage = exception.Failure switch
            {
                RemoteLocalizationAuthoringFailure.Unauthorized => "Accedi con myZenkai per modificare lo staging.",
                RemoteLocalizationAuthoringFailure.Network => "Network error: impossibile acquisire il lock.",
                _ => exception.Message,
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _authoringMessage = exception.Message;
        }
        finally
        {
            IsAuthoringBusy = false;
            NotifyAuthoringPropertiesChanged();
        }
    }

    public async Task SaveToStagingAsync()
    {
        if (_authoringSession?.EditSession is null)
        {
            return;
        }
        IsAuthoringBusy = true;
        try
        {
            var result = await _authoringService.SaveAsync(_project, _authoringSession);
            _authoringMessage = result.LocalMirrorSynchronized
                ? "Remote staging saved · local mirror synchronized."
                : result.Message ?? "Nessuna modifica locale da inviare.";
            RefreshStoryProjection();
        }
        catch (RemoteLocalizationAuthoringException exception)
        {
            _authoringMessage = exception.Failure == RemoteLocalizationAuthoringFailure.Network
                ? "Network error: il salvataggio nello staging non è stato confermato."
                : exception.Message;
        }
        finally
        {
            IsAuthoringBusy = false;
            NotifyAuthoringPropertiesChanged();
        }
    }

    public void UndoLocalization()
    {
        if (_authoringSession?.EditSession?.Undo() == true)
        {
            _authoringSession.State = LocalizationAuthoringState.LocalChanges;
            RefreshStoryProjection();
        }
    }

    public void RedoLocalization()
    {
        if (_authoringSession?.EditSession?.Redo() == true)
        {
            _authoringSession.State = LocalizationAuthoringState.LocalChanges;
            RefreshStoryProjection();
        }
    }

    public void DiscardLocalization()
    {
        if (_authoringSession?.EditSession is null)
        {
            return;
        }
        _authoringSession.EditSession.Discard();
        _authoringSession.State = _authoringSession.EditSession.Snapshot.HasStaging
            ? LocalizationAuthoringState.Staging : LocalizationAuthoringState.Clean;
        _authoringMessage = "Modifiche locali scartate; lo staging remoto non è stato modificato.";
        RefreshStoryProjection();
    }

    public async Task RetryMirrorAsync()
    {
        if (_authoringSession is null)
        {
            return;
        }
        IsAuthoringBusy = true;
        try
        {
            var result = await _authoringService.RetryMirrorAsync(_project, _authoringSession);
            _authoringMessage = result.LocalMirrorSynchronized
                ? "Local mirror synchronized con lo staging remoto."
                : result.Message ?? "Mirror locale ancora non sincronizzabile.";
            RefreshStoryProjection();
        }
        finally
        {
            IsAuthoringBusy = false;
            NotifyAuthoringPropertiesChanged();
        }
    }

    public async Task ReleaseAsync()
    {
        StopLockHeartbeat();
        var session = _authoringSession;
        if (session?.EditSession is not null)
        {
            session.EditSession.PropertyChanged -= EditSession_PropertyChanged;
            try
            {
                await _authoringService.ReleaseAsync(_project, session);
            }
            catch (Exception exception) when (exception is RemoteLocalizationAuthoringException or IOException)
            {
                // Lock expiration is deliberately the fallback for an interrupted close/project change.
            }
        }
        _authoringSession = null;
        NotifyAuthoringPropertiesChanged();
    }

    public void Dispose()
    {
        StopLockHeartbeat();
        Navigator.LocationSelected -= Navigator_LocationSelected;
        Navigator.Dispose();
        if (_authoringSession?.EditSession is { } editSession)
        {
            editSession.PropertyChanged -= EditSession_PropertyChanged;
        }
        // The window close path cannot await IDisposable. The API itself treats release as best effort.
        if (_authoringSession is { EditSession: not null } session)
        {
            _ = _authoringService.ReleaseAsync(_project, session);
        }
    }

    private void SetLocalizationFieldValue(LocalizationReferenceOrigin origin, string value)
    {
        if (_authoringSession?.State is LocalizationAuthoringState.Conflict or
            LocalizationAuthoringState.LockedByOther or LocalizationAuthoringState.LocalOutOfSync)
        {
            return;
        }
        if (_authoringSession?.EditSession?.SetValue(origin, value) == true)
        {
            _authoringSession.State = LocalizationAuthoringState.LocalChanges;
            RefreshStoryProjection();
        }
    }

    private void EditSession_PropertyChanged(object? sender, PropertyChangedEventArgs args) =>
        NotifyAuthoringPropertiesChanged();

    private void StartLockHeartbeat()
    {
        StopLockHeartbeat();
        _lockHeartbeatCancellation = new CancellationTokenSource();
        _ = RenewLockLoopAsync(_lockHeartbeatCancellation.Token);
    }

    private async Task RenewLockLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_authoringSession is not { EditSession: not null } session)
                {
                    return;
                }
                try
                {
                    session.UpdateLock(await _authoringService.RenewAsync(_project, session, cancellationToken));
                    NotifyAuthoringPropertiesChanged();
                }
                catch (RemoteLocalizationAuthoringException exception)
                {
                    session.State = exception.Failure == RemoteLocalizationAuthoringFailure.LockedByOther
                        ? LocalizationAuthoringState.LockedByOther
                        : LocalizationAuthoringState.Conflict;
                    _authoringMessage = "Lock perso o non rinnovabile: salva è bloccato finché non ricarichi la base.";
                    NotifyAuthoringPropertiesChanged();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopLockHeartbeat()
    {
        _lockHeartbeatCancellation?.Cancel();
        _lockHeartbeatCancellation?.Dispose();
        _lockHeartbeatCancellation = null;
    }

    private void RefreshStoryProjection()
    {
        (int Start, int End)? selectedLocation = _selectedBlock is null
            ? null
            : (Start: _selectedBlock.CommandStartIndex, End: _selectedBlock.CommandEndIndex);
        _blocks = SelectedSource?.Blocks.Select(ProjectBlockForAuthoring).ToArray() ?? [];
        if (selectedLocation is not null)
        {
            _selectedBlock = _blocks.FirstOrDefault(block =>
                block.CommandStartIndex == selectedLocation.Value.Start && block.CommandEndIndex == selectedLocation.Value.End) ??
                _blocks.FirstOrDefault();
        }
        OnPropertyChanged(nameof(Blocks));
        OnPropertyChanged(nameof(SelectedBlock));
        OnPropertyChanged(nameof(SelectedBlockDetails));
        OnPropertyChanged(nameof(SelectedBlockRawCommands));
        NotifyAuthoringPropertiesChanged();
        // The index follows the same working localization overlay used by the
        // timeline. Rebuilding is debounced by the navigator.
        Navigator.RefreshSearchIndex(ProjectBlockForAuthoring);
    }

    private async Task ReloadWorkspaceAsync()
    {
        var previousLocation = GetCurrentLocation();
        Document = await _reloadWorkspace();
        Navigator.Reload(Document.Workspace, previousLocation);
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(WorkspaceStatusText));
        RefreshStoryProjection();
    }

    private void Navigator_LocationSelected(object? sender, StoryLocation location) => NavigateToLocation(location);

    private void NavigateToLocation(StoryLocation location)
    {
        if (location.SourceKind == StoryLocationSourceKind.CommonEvent)
        {
            var commonEvent = Document.Workspace.CommonEvents.FirstOrDefault(@event => @event.Id == location.EventId);
            if (commonEvent is null)
            {
                return;
            }
            SelectedSource = StoryNavigationItem.Source(
                commonEvent.DisplayName,
                0,
                $"Common Event · Trigger {commonEvent.Trigger} · Switch {commonEvent.SwitchId}",
                commonEvent.Blocks,
                commonEvent.CommandListTarget);
            SelectBlockAt(location, commonEvent.Blocks);
            return;
        }

        var map = Document.Workspace.Maps.FirstOrDefault(candidate => candidate.Id == location.MapId);
        var @event = map?.Events.FirstOrDefault(candidate => candidate.Id == location.EventId);
        var page = @event?.Pages.FirstOrDefault(candidate => candidate.Number == location.PageNumber);
        if (map is null || @event is null || page is null)
        {
            return;
        }
        SelectedSource = StoryNavigationItem.Source(
            page.DisplayName,
            0,
            $"{map.DisplayName} › Event {@event.Id} {@event.Name} › {page.ConditionsSummary}",
            page.Blocks,
            page.CommandListTarget);
        SelectBlockAt(location, page.Blocks);
    }

    private void SelectBlockAt(StoryLocation location, IReadOnlyList<StoryBlock> blocks)
    {
        if (blocks.Count == 0 || location.CommandStartIndex is null)
        {
            return;
        }
        var block = blocks.FirstOrDefault(candidate =>
                        candidate.CommandStartIndex == location.CommandStartIndex &&
                        candidate.CommandEndIndex == location.CommandEndIndex) ??
                    blocks.OrderBy(candidate => Math.Abs(candidate.CommandStartIndex - location.CommandStartIndex.Value))
                        .ThenBy(candidate => candidate.CommandStartIndex)
                        .First();
        SelectedBlock = Blocks.FirstOrDefault(candidate =>
                            candidate.CommandStartIndex == block.CommandStartIndex &&
                            candidate.CommandEndIndex == block.CommandEndIndex) ??
                        ProjectBlockForAuthoring(block);
    }

    private StoryLocation? GetCurrentLocation()
    {
        var target = SelectedSource?.Target;
        if (target is null)
        {
            return null;
        }
        if (target.Kind == StoryCommandListKind.CommonEvent)
        {
            return StoryLocation.ForCommonEvent(target.EventId, SelectedBlock);
        }
        if (target.MapId is not { } mapId || target.PageNumber is not { } pageNumber)
        {
            return null;
        }
        return SelectedBlock is null
            ? StoryLocation.ForPage(mapId, target.EventId, pageNumber)
            : StoryLocation.ForBlock(mapId, target.EventId, pageNumber, SelectedBlock);
    }

    private void NotifyAuthoringPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasAuthoringSession));
        OnPropertyChanged(nameof(CanBeginEditing));
        OnPropertyChanged(nameof(CanSaveToStaging));
        OnPropertyChanged(nameof(CanUndoLocalization));
        OnPropertyChanged(nameof(CanRedoLocalization));
        OnPropertyChanged(nameof(CanDiscardLocalization));
        OnPropertyChanged(nameof(CanRetryMirror));
        OnPropertyChanged(nameof(AuthoringStateText));
        OnPropertyChanged(nameof(AuthoringMessage));
        OnPropertyChanged(nameof(LocalizationFields));
    }

    private static bool IsSameFile(LocalizationReferenceOrigin left, LocalizationReferenceOrigin right) =>
        left.Locale.Equals(right.Locale, StringComparison.OrdinalIgnoreCase) &&
        left.SourceFile.Equals(right.SourceFile, StringComparison.OrdinalIgnoreCase);

    private StoryBlock ProjectBlockForAuthoring(StoryBlock block)
    {
        if (_authoringSession?.EditSession is not { } editSession)
        {
            return block;
        }
        var title = block.Title;
        var displayText = block.DisplayText;
        foreach (var origin in block.LocalizationOrigins.Where(origin =>
                     IsSameFile(origin, _authoringSession.Origin) &&
                     origin.ResolvedValue is not null &&
                     editSession.TryGetValue(origin, out _)))
        {
            if (!editSession.TryGetValue(origin, out var currentValue) ||
                string.Equals(origin.ResolvedValue, currentValue, StringComparison.Ordinal))
            {
                continue;
            }
            title = title.Replace(origin.ResolvedValue!, currentValue, StringComparison.Ordinal);
            displayText = displayText.Replace(origin.ResolvedValue!, currentValue, StringComparison.Ordinal);
        }
        return block with {Title = title, DisplayText = displayText};
    }

    private static bool IsMasterEditable(LocalizationReferenceOrigin origin) =>
        origin.Locale.Equals(LocalizationService.DefaultLocale, StringComparison.OrdinalIgnoreCase) &&
        origin.Segments.Count > 0;

    private static string GetFieldLabel(StoryBlock block, int index, LocalizationReferenceOrigin origin) =>
        block.Kind == StoryBlockKind.Dialogue && index == 0 ? "Speaker" :
        block.Kind == StoryBlockKind.Dialogue && index == 1 ? "Text" :
        $"Localized value · {origin.Path}";

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class StoryLocalizationFieldViewModel : INotifyPropertyChanged
{
    private readonly Action<LocalizationReferenceOrigin, string> _setValue;
    private string _value;

    public StoryLocalizationFieldViewModel(
        string label,
        LocalizationReferenceOrigin origin,
        string value,
        Action<LocalizationReferenceOrigin, string> setValue,
        bool isReadOnly)
    {
        Label = label;
        Origin = origin;
        _value = value;
        _setValue = setValue;
        IsReadOnly = isReadOnly;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }
    public LocalizationReferenceOrigin Origin { get; }
    public string Path => $"{Origin.SourceFile} · {Origin.Path}";
    public bool IsReadOnly { get; }

    public string Value
    {
        get => _value;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_value, value, StringComparison.Ordinal))
            {
                return;
            }
            _setValue(Origin, value);
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}

public sealed record StoryNavigationItem
{
    public required string Title { get; init; }
    public required int Depth { get; init; }
    public string Context { get; init; } = string.Empty;
    public IReadOnlyList<StoryBlock> Blocks { get; init; } = [];
    public StoryCommandListTarget? Target { get; init; }
    public bool IsSelectable { get; init; }

    public string DisplayTitle => string.Concat(Enumerable.Repeat("   ", Depth)) + Title;
    public string KindText => IsSelectable ? "Timeline" : "";

    public static StoryNavigationItem Group(string title, int depth, string? context = null) => new()
    {
        Title = title,
        Depth = depth,
        Context = context ?? string.Empty,
    };

    public static StoryNavigationItem Source(
        string title,
        int depth,
        string context,
        IReadOnlyList<StoryBlock> blocks,
        StoryCommandListTarget? target) => new()
    {
        Title = title,
        Depth = depth,
        Context = context,
        Blocks = blocks,
        Target = target,
        IsSelectable = true,
    };
}
