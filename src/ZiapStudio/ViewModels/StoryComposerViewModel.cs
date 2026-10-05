using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Fusion.Story;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.ViewModels;

/// <summary>
/// UI state only. It builds and previews an immutable plan; all mutating work
/// remains in StoryCompositionService.
/// </summary>
public sealed class StoryComposerViewModel : INotifyPropertyChanged
{
    private readonly ZiapProject _project;
    private readonly StoryCompositionService _service;
    private readonly Func<Task> _refreshWorkspace;
    private readonly Func<string?> _retainedLockAcquiredAt;
    private StoryCommandListTarget? _target;
    private StoryBlock? _anchor;
    private StoryCompositionOperationType _operationType;
    private string _speaker = string.Empty;
    private string _text = string.Empty;
    private StoryCompositionPlan? _plan;
    private StoryCompositionPreview? _preview;
    private IReadOnlyList<StoryCompositionRecoveryRecord> _recoveries = [];
    private bool _isBusy;
    private string _message = "Seleziona un dialogo/narrazione localizzato per aggiungere un blocco dopo di esso.";

    public StoryComposerViewModel(
        ZiapProject project,
        StoryCompositionService service,
        Func<Task> refreshWorkspace,
        Func<string?> retainedLockAcquiredAt)
    {
        _project = project;
        _service = service;
        _refreshWorkspace = refreshWorkspace;
        _retainedLockAcquiredAt = retainedLockAcquiredAt;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string Speaker
    {
        get => _speaker;
        set
        {
            if (SetProperty(ref _speaker, value ?? string.Empty)) InvalidatePlan();
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty)) InvalidatePlan();
        }
    }

    public bool IsDialogue => _operationType == StoryCompositionOperationType.AddDialogue;
    public bool IsNarration => _operationType == StoryCompositionOperationType.AddNarration;
    public bool CanStart => !IsBusy && HasCompatibleAnchor;
    public bool CanPreview => CanStart && (IsNarration || !string.IsNullOrWhiteSpace(Speaker)) &&
        !string.IsNullOrWhiteSpace(Text);
    public bool CanCommit => !IsBusy && _plan is not null;
    public bool CanUnlink => !IsBusy && _target is not null && _anchor is { Kind: StoryBlockKind.Dialogue };
    public string Message => _message;
    public bool HasRecovery => _recoveries.Count > 0;
    public string RecoveryMessage => !HasRecovery ? string.Empty :
        "Una modifica Story è stata salvata nello staging ZIAP ma non completata nel file RPG Maker. " +
        "Completa l'inserimento locale o dismiss: dismiss non elimina la Localization.";
    public string PreviewText => _preview is null ?
        "Preview: nessuna mutazione viene eseguita finché non scegli Add to story." :
        $"LOCALIZATION\n{_preview.LocalizationSummary}\nbranch: {FormatBranch(_preview.Plan.LocalizationBranchPath)}\n" +
        $"values: {(IsDialogue ? $"name = {Speaker}\n" : string.Empty)}text = {Text}\n\n" +
        $"RPG MAKER\n{_preview.EventSummary}\n+ 101 Show Text\n+ 401 Text";

    public void SetAnchor(StoryCommandListTarget? target, StoryBlock? anchor)
    {
        _target = target;
        _anchor = anchor;
        InvalidatePlan();
        if (!HasCompatibleAnchor)
        {
            _message = "Questa posizione non espone una destination Localization strutturata sicura.";
        }
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanUnlink));
        OnPropertyChanged(nameof(Message));
    }

    public void Start(StoryCompositionOperationType operationType)
    {
        _operationType = operationType;
        if (operationType == StoryCompositionOperationType.AddDialogue && string.IsNullOrWhiteSpace(Speaker))
        {
            Speaker = _anchor?.Kind == StoryBlockKind.Dialogue ? _anchor.Title : string.Empty;
        }
        _message = operationType == StoryCompositionOperationType.AddDialogue
            ? "Composer dialogo: presentation ereditata dal dialogo selezionato quando disponibile."
            : "Composer narrazione: non verrà creata una fake localization .name.";
        OnPropertyChanged(nameof(IsDialogue));
        OnPropertyChanged(nameof(IsNarration));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(Message));
    }

    public async Task PreviewAsync()
    {
        if (!CanPreview || _target is null || _anchor is null) return;
        IsBusy = true;
        try
        {
            var plan = await _service.BuildPlanAsync(
                _operationType, _target, _anchor, _project.Path, Speaker.Trim(), Text, CancellationToken.None);
            _plan = await _service.PrepareAsync(_project, plan, CancellationToken.None);
            _preview = _service.CreatePreview(_plan);
            _message = "Preview pronto. L'indice indicato è previsto: il server assegnerà quello autorevole al commit.";
        }
        catch (Exception exception) when (exception is StoryCompositionValidationException or
            StoryCommandListWriteException or IOException or InvalidOperationException)
        {
            _plan = null;
            _preview = null;
            _message = exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCompositionProperties();
        }
    }

    public async Task CommitAsync()
    {
        if (_plan is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _service.CommitAsync(
                _project, _plan, _retainedLockAcquiredAt(), CancellationToken.None);
            _message = result.IsSuccess
                ? "Story salvata: MDV append-only, mirror locale e command RPG Maker aggiornati."
                : result.Message ?? "La composizione non è stata completata.";
            if (result.IsSuccess)
            {
                await _refreshWorkspace();
                _plan = null;
                _preview = null;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _message = exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCompositionProperties();
        }
    }

    public async Task UnlinkAsync()
    {
        if (!CanUnlink || _target is null || _anchor is null) return;
        IsBusy = true;
        try
        {
            var result = await _service.UnlinkAsync(_project, _target, _anchor, CancellationToken.None);
            _message = result.IsSuccess
                ? "Il blocco è stato rimosso dall'evento. La entry Localization non è stata eliminata."
                : result.Message ?? "Il blocco non è stato rimosso.";
            if (result.IsSuccess) await _refreshWorkspace();
        }
        finally
        {
            IsBusy = false;
            NotifyCompositionProperties();
        }
    }

    public async Task LoadRecoveriesAsync()
    {
        try
        {
            _recoveries = await _service.FindRecoveriesAsync(_project, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or StoryCompositionRecoveryException)
        {
            _message = exception.Message;
        }
        finally
        {
            OnPropertyChanged(nameof(HasRecovery));
            OnPropertyChanged(nameof(RecoveryMessage));
            OnPropertyChanged(nameof(Message));
        }
    }

    public async Task CompleteRecoveryAsync()
    {
        var recovery = _recoveries.FirstOrDefault();
        if (recovery is null || IsBusy) return;
        IsBusy = true;
        try
        {
            StoryCompositionPlan? rebase = null;
            if (recovery.State == StoryCompositionState.SourceConflict && _target is not null && _anchor is not null)
            {
                rebase = await _service.BuildPlanAsync(
                    recovery.Plan.OperationType,
                    _target,
                    _anchor,
                    _project.Path,
                    recovery.Plan.Values.Speaker,
                    recovery.Plan.Values.Text,
                    CancellationToken.None);
            }
            var result = await _service.CompleteRecoveryAsync(_project, recovery, rebase, CancellationToken.None);
            _message = result.IsSuccess
                ? "Recovery completato usando la entry MDV già assegnata; non è stato eseguito un secondo append."
                : result.Message ?? "Recovery non completato.";
            if (result.IsSuccess) await _refreshWorkspace();
            await LoadRecoveriesAsync();
        }
        catch (Exception exception) when (exception is StoryCompositionValidationException or IOException or InvalidOperationException)
        {
            _message = exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCompositionProperties();
        }
    }

    public async Task DismissRecoveryAsync()
    {
        var recovery = _recoveries.FirstOrDefault();
        if (recovery is null || IsBusy) return;
        IsBusy = true;
        try
        {
            await _service.DismissRecoveryAsync(recovery.Plan.OperationId, CancellationToken.None);
            _message = "Recovery dismiss: l'entry Localization remota resta intenzionalmente disponibile e può risultare non utilizzata.";
            await LoadRecoveriesAsync();
        }
        finally
        {
            IsBusy = false;
            NotifyCompositionProperties();
        }
    }

    // The 0.3A parser represents both Show Text forms as Dialogue blocks; a
    // narration is the compatible form with an empty speaker parameter.
    private bool HasCompatibleAnchor => _target is not null && _anchor is { Kind: StoryBlockKind.Dialogue } &&
        _anchor.LocalizationOrigins.Any(origin => origin.Segments.Count >= 2 &&
            origin.Segments[^1].PropertyName == "text" &&
            origin.Locale.Equals(LocalizationService.DefaultLocale, StringComparison.OrdinalIgnoreCase));

    private void InvalidatePlan()
    {
        _plan = null;
        _preview = null;
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CanPreview));
    }

    private void NotifyCompositionProperties()
    {
        OnPropertyChanged(nameof(IsDialogue));
        OnPropertyChanged(nameof(IsNarration));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CanUnlink));
        OnPropertyChanged(nameof(HasRecovery));
        OnPropertyChanged(nameof(RecoveryMessage));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(PreviewText));
    }

    private static string FormatBranch(IReadOnlyList<ZiapStudio.Core.Localization.LocalizationPathSegment> branch) =>
        string.Join(" / ", branch.Select(segment => segment.PropertyName ?? $"[{segment.ArrayIndex}]"));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
