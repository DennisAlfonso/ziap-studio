using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Audio;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;
using ZiapStudio.Core.Preflight;
using ZiapStudio.Platform.Windows;
using ZiapStudio.Services;
using ZiapStudio.Services.Assets;
using ZiapStudio.Services.Authentication;
using ZiapStudio.Services.Documents;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Initialization;
using ZiapStudio.Services.Fusion.Preflight;
using ZiapStudio.Services.Fusion.Audio;
using ZiapStudio.Services.Fusion.Bosses;
using ZiapStudio.Services.Integration.Console;
using ZiapStudio.Services.Integration.Remote;
using ZiapStudio.Services.Providers;

namespace ZiapStudio.ViewModels;

public sealed class MainPageViewModel : INotifyPropertyChanged
{
    private readonly ProjectService _projectService;
    private readonly ProjectInitializationService _projectInitializationService;
    private readonly ProjectIdGenerator _projectIdGenerator;
    private readonly ProjectProviderService _projectProviderService;
    private readonly DocumentService _documentService;
    private readonly AssetPreviewService _assetPreviewService;
    private readonly DocumentEditSessionFactory _editSessionFactory;
    private readonly DocumentSaveService _documentSaveService;
    private readonly FusionBossAuthoringService _fusionBossAuthoringService;
    private readonly IRecentProjectService _recentProjectService;
    private readonly WindowsFolderPickerService _folderPickerService;
    private readonly WindowsShellService _shellService;
    private readonly ConsoleIntegrationService _consoleIntegrationService;
    private readonly RemoteLocalizationService _remoteLocalizationService;
    private readonly PublishedLocalizationSyncService _publishedLocalizationSyncService;
    private readonly ZiapAuthenticationService _authenticationService;
    private readonly RemoteLocalizationDocumentViewModel _remoteLocalizationDocument = new();
    private readonly PreflightScanner _preflightScanner;
    private readonly PreflightSuppressionStore _preflightSuppressionStore;
    private readonly FusionAudioCatalogService _fusionAudioCatalogService;
    private readonly FusionAudioPlaybackResolver _fusionAudioPlaybackResolver;
    private readonly AudioPreviewService _audioPreviewService;
    private readonly PreflightDocumentViewModel _preflightDocument = new();
    private ZiapProject? _currentProject;
    private string? _errorMessage;
    private string _statusMessage = "Scegli una cartella per iniziare.";
    private bool _isBusy;
    private bool _isInitialized;
    private DocumentTabViewModel? _selectedDocument;
    private bool _isRemoteLocalizationChecking;
    private string _remoteLocalizationMessage = "Apri un progetto ZIAP per verificare la localizzazione.";
    private int _remoteLocalizationRequestVersion;
    private bool _isAuthenticationBusy;
    private bool _isPreflightScanning;
    private PreflightScanResult _preflightResult = PreflightScanResult.Empty;

    public MainPageViewModel(
        ProjectService projectService,
        ProjectInitializationService projectInitializationService,
        ProjectIdGenerator projectIdGenerator,
        ProjectProviderService projectProviderService,
        DocumentService documentService,
        AssetPreviewService assetPreviewService,
        DocumentEditSessionFactory editSessionFactory,
        DocumentSaveService documentSaveService,
        FusionBossAuthoringService fusionBossAuthoringService,
        IRecentProjectService recentProjectService,
        WindowsFolderPickerService folderPickerService,
        WindowsShellService shellService,
        ConsoleIntegrationService consoleIntegrationService,
        RemoteLocalizationService remoteLocalizationService,
        PublishedLocalizationSyncService publishedLocalizationSyncService,
        ZiapAuthenticationService authenticationService,
        PreflightScanner preflightScanner,
        PreflightSuppressionStore preflightSuppressionStore,
        FusionAudioCatalogService fusionAudioCatalogService,
        FusionAudioPlaybackResolver fusionAudioPlaybackResolver,
        AudioPreviewService audioPreviewService)
    {
        _projectService = projectService;
        _projectInitializationService = projectInitializationService;
        _projectIdGenerator = projectIdGenerator;
        _projectProviderService = projectProviderService;
        _documentService = documentService;
        _assetPreviewService = assetPreviewService;
        _editSessionFactory = editSessionFactory;
        _documentSaveService = documentSaveService;
        _fusionBossAuthoringService = fusionBossAuthoringService;
        _recentProjectService = recentProjectService;
        _folderPickerService = folderPickerService;
        _shellService = shellService;
        _consoleIntegrationService = consoleIntegrationService;
        _remoteLocalizationService = remoteLocalizationService;
        _publishedLocalizationSyncService = publishedLocalizationSyncService;
        _authenticationService = authenticationService;
        _preflightScanner = preflightScanner;
        _preflightSuppressionStore = preflightSuppressionStore;
        _fusionAudioCatalogService = fusionAudioCatalogService;
        _fusionAudioPlaybackResolver = fusionAudioPlaybackResolver;
        _audioPreviewService = audioPreviewService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ZiapProject> RecentProjects { get; } = [];

    public ObservableCollection<ProjectExplorerItemViewModel> ProjectExplorerNodes { get; } = [];

    public ObservableCollection<DocumentTabViewModel> OpenDocuments { get; } = [];

    public ObservableCollection<RemoteLocalizationFileViewModel> RemoteLocalizationFiles { get; } = [];

    public ObservableCollection<RemoteLocalizationFileViewModel> RemoteLocalizationRecentProblems { get; } = [];

    public RemoteLocalizationDocumentViewModel RemoteLocalizationDocument =>
        _remoteLocalizationDocument;

    public PreflightDocumentViewModel PreflightDocument => _preflightDocument;

    public ProjectIdGenerator ProjectIdGenerator => _projectIdGenerator;

    public ZiapProject? CurrentProject
    {
        get => _currentProject;
        private set
        {
            if (SetProperty(ref _currentProject, value))
            {
                NotifyProjectPropertiesChanged();
            }
        }
    }

    public bool HasProject => CurrentProject is not null;

    public bool HasNoProject => CurrentProject is null;

    public bool NeedsInitialization => CurrentProject is { IsZiapInitialized: false };

    public bool CanInitialize => NeedsInitialization && CanInteract;

    public string ProjectStateLabel => CurrentProject?.IsZiapInitialized == true
        ? "ZIAP PROJECT"
        : "DETECTED";

    public string ProjectName => CurrentProject?.Name ?? string.Empty;

    public string ProjectExplorerTitle => CurrentProject?.Name ?? "Project Explorer";

    public string PackageName => ValueOrDash(CurrentProject?.PackageName);

    public string ProjectVersion => ValueOrDash(CurrentProject?.Version);

    public string ProjectType => CurrentProject?.ProjectTypeDisplayName ?? "—";

    public string Publisher => ValueOrDash(CurrentProject?.Publisher);

    public string ProjectPath => CurrentProject?.Path ?? string.Empty;

    public bool HasRemoteLocalizationCard => CurrentProject?.IsZiapInitialized == true;

    public bool HasPreflightCard => CurrentProject?.IsZiapInitialized == true;

    public bool HasRemoteLocalizationFiles => RemoteLocalizationFiles.Count > 0;

    public bool IsAuthenticated => _authenticationService.IsAuthenticated;

    public bool IsNotAuthenticated => !IsAuthenticated;

    public string AccountDisplayName =>
        _authenticationService.CurrentAccount?.DisplayName ?? "Non connesso";

    public string AccountDetail =>
        _authenticationService.CurrentAccount?.Detail ?? "myZenkai Account";

    public bool IsAuthenticationBusy
    {
        get => _isAuthenticationBusy;
        private set
        {
            if (SetProperty(ref _isAuthenticationBusy, value))
            {
                OnPropertyChanged(nameof(CanSignIn));
                OnPropertyChanged(nameof(CanSignOut));
            }
        }
    }

    public bool CanSignIn => IsNotAuthenticated && !IsAuthenticationBusy;

    public bool CanSignOut => IsAuthenticated && !IsAuthenticationBusy;

    public bool IsRemoteLocalizationChecking
    {
        get => _isRemoteLocalizationChecking;
        private set
        {
            if (SetProperty(ref _isRemoteLocalizationChecking, value))
            {
                OnPropertyChanged(nameof(CanRefreshRemoteLocalization));
            }
        }
    }

    public bool CanRefreshRemoteLocalization =>
        CurrentProject?.IsZiapInitialized == true && !IsRemoteLocalizationChecking;

    public bool IsPreflightScanning
    {
        get => _isPreflightScanning;
        private set
        {
            if (SetProperty(ref _isPreflightScanning, value))
            {
                OnPropertyChanged(nameof(CanAnalyzePreflight));
            }
        }
    }

    public bool CanAnalyzePreflight =>
        CurrentProject?.IsZiapInitialized == true && !IsPreflightScanning;

    public string RemoteLocalizationMessage
    {
        get => _remoteLocalizationMessage;
        private set => SetProperty(ref _remoteLocalizationMessage, value);
    }

    public DocumentTabViewModel? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (SetProperty(ref _selectedDocument, value))
            {
                OnPropertyChanged(nameof(IsProjectOverviewSelected));
                OnPropertyChanged(nameof(IsRemoteLocalizationDocumentSelected));
                OnPropertyChanged(nameof(IsPreflightDocumentSelected));
                OnPropertyChanged(nameof(IsFusionAudioDocumentSelected));
                OnPropertyChanged(nameof(IsFusionBossDocumentSelected));
                OnPropertyChanged(nameof(IsDatabaseDocumentSelected));
                OnPropertyChanged(nameof(IsEditingDocumentSelected));
                OnPropertyChanged(nameof(ActiveDatabaseDocument));
                OnPropertyChanged(nameof(ActiveFusionAudioDocument));
                OnPropertyChanged(nameof(ActiveFusionBossDocument));
                NotifyEditingPropertiesChanged();
            }
        }
    }

    public bool IsProjectOverviewSelected => SelectedDocument?.IsProjectOverview == true;

    public bool IsRemoteLocalizationDocumentSelected =>
        SelectedDocument?.IsRemoteLocalization == true;

    public bool IsPreflightDocumentSelected => SelectedDocument?.IsPreflight == true;

    public bool IsFusionAudioDocumentSelected => SelectedDocument?.IsFusionAudio == true;

    public bool IsFusionBossDocumentSelected => SelectedDocument?.IsFusionBoss == true;

    public bool IsDatabaseDocumentSelected => ActiveDatabaseDocument is not null;

    public bool IsEditingDocumentSelected =>
        IsDatabaseDocumentSelected ||
        IsFusionAudioDocumentSelected ||
        IsFusionBossDocumentSelected;

    public RpgMakerDatabaseDocumentViewModel? ActiveDatabaseDocument => SelectedDocument?.Database;

    public FusionAudioDocumentViewModel? ActiveFusionAudioDocument =>
        SelectedDocument?.FusionAudio;

    public FusionBossDocumentViewModel? ActiveFusionBossDocument =>
        SelectedDocument?.FusionBoss;

    public bool CanSaveDocument => CanInteract && SelectedDocument?.IsDirty == true;

    public bool CanSaveAll => CanInteract && OpenDocuments.Any(document => document.IsDirty);

    public bool CanUndo => CanInteract && SelectedDocument?.CanUndo == true;

    public bool CanRedo => CanInteract && SelectedDocument?.CanRedo == true;

    public IReadOnlyList<DocumentTabViewModel> DirtyDocuments =>
        OpenDocuments.Where(document => document.IsDirty).ToArray();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanInteract));
                OnPropertyChanged(nameof(CanInitialize));
                NotifyEditingPropertiesChanged();
            }
        }
    }

    public bool CanInteract => !IsBusy;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        try
        {
            await _authenticationService.InitializeAsync();
        }
        catch (AuthenticationException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ErrorMessage = "Impossibile leggere la sessione protetta da Windows Credential Manager.";
        }
        finally
        {
            NotifyAuthenticationPropertiesChanged();
        }

        try
        {
            ReplaceRecentProjects(await _recentProjectService.GetRecentProjectsAsync());
        }
        catch (SettingsException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public async Task SignInAsync()
    {
        if (!CanSignIn)
        {
            return;
        }

        ClearError();
        IsAuthenticationBusy = true;
        try
        {
            await _authenticationService.SignInAsync();
            StatusMessage = $"Connesso a myZenkai come {AccountDisplayName}.";
        }
        catch (AuthenticationException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ErrorMessage = "Windows Credential Manager non ha potuto salvare la sessione myZenkai.";
        }
        finally
        {
            IsAuthenticationBusy = false;
            NotifyAuthenticationPropertiesChanged();
        }

        if (IsAuthenticated && CurrentProject?.IsZiapInitialized == true)
        {
            await RefreshRemoteLocalizationAsync();
        }
    }

    public async Task SignOutAsync()
    {
        if (!CanSignOut)
        {
            return;
        }

        ClearError();
        IsAuthenticationBusy = true;
        try
        {
            await _authenticationService.SignOutAsync();
            StatusMessage = "Sessione myZenkai disconnessa da ZIAP Studio.";
            ResetRemoteLocalizationStatus(CurrentProject?.IsZiapInitialized == true
                ? "Accedi con myZenkai per verificare le versioni pubblicate."
                : "Apri un progetto ZIAP per verificare la localizzazione.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ErrorMessage = "Windows Credential Manager non ha potuto rimuovere la sessione myZenkai.";
        }
        finally
        {
            IsAuthenticationBusy = false;
            NotifyAuthenticationPropertiesChanged();
        }
    }

    public async Task OpenProjectAsync()
    {
        ClearError();

        try
        {
            var path = await _folderPickerService.PickProjectFolderAsync();
            if (path is not null)
            {
                await LoadProjectAsync(path);
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Impossibile mostrare il selettore di cartelle: {exception.Message}";
        }
    }

    public Task OpenRecentProjectAsync(ZiapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ClearError();
        return LoadProjectAsync(project.Path);
    }

    public ProjectInitializationOptions CreateInitializationDefaults()
    {
        var project = CurrentProject
            ?? throw new InvalidOperationException("Nessun progetto selezionato.");

        return new ProjectInitializationOptions
        {
            Name = project.Name,
            Id = _projectIdGenerator.Generate(project.Name),
            Type = project.ProjectType,
            Version = project.Version,
            Publisher = project.Publisher ?? "Zenkaiverse",
        };
    }

    public async Task InitializeProjectAsync(ProjectInitializationOptions options)
    {
        if (CurrentProject is null || CurrentProject.IsZiapInitialized)
        {
            return;
        }

        ClearError();
        IsBusy = true;
        var projectPath = CurrentProject.Path;
        var initialized = false;

        try
        {
            await _projectInitializationService.InitializeAsync(projectPath, options);
            var initializedProject = await _projectService.LoadAsync(projectPath);
            CurrentProject = initializedProject;
            ResetDocumentWorkspace(initializedProject);
            await RefreshProjectExplorerAsync(initializedProject);
            StatusMessage = $"{initializedProject.ProjectTypeDisplayName} • {initializedProject.Path}";
            initialized = true;

            try
            {
                ReplaceRecentProjects(await _recentProjectService.AddAsync(initializedProject));
            }
            catch (SettingsException exception)
            {
                ErrorMessage = exception.Message;
            }
        }
        catch (Exception exception) when (exception is ProjectInitializationException or ProjectLoadException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (initialized)
        {
            await RefreshRemoteLocalizationAsync();
        }
    }

    public void OpenCurrentProjectFolder()
    {
        if (CurrentProject is null)
        {
            return;
        }

        ClearError();

        try
        {
            _shellService.OpenFolder(CurrentProject.Path);
        }
        catch (Exception exception) when (exception is DirectoryNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ErrorMessage = "Impossibile aprire la cartella del progetto in Esplora file.";
        }
    }

    public void ClearError() => ErrorMessage = null;

    public void OpenLocalizationInConsole(LocalizationReferenceOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (CurrentProject is null)
        {
            return;
        }

        ClearError();
        try
        {
            _consoleIntegrationService.Open(new ConsoleNavigationTarget(
                ProjectId: CurrentProject.Id,
                Area: ConsoleNavigationArea.Localization,
                Language: origin.Locale,
                File: origin.SourceFile,
                Focus: origin.Path));
            StatusMessage =
                $"Aperta ZIAP Console su {origin.SourceFile} · {origin.Path}.";
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                NotSupportedException or System.ComponentModel.Win32Exception)
        {
            ErrorMessage = "Impossibile aprire la risorsa in ZIAP Console.";
        }
    }

    public async Task RefreshRemoteLocalizationAsync()
    {
        var project = CurrentProject;
        if (project?.IsZiapInitialized != true)
        {
            ResetRemoteLocalizationStatus(
                "Inizializza il progetto ZIAP per verificare le versioni pubblicate.");
            return;
        }

        var requestVersion = Interlocked.Increment(ref _remoteLocalizationRequestVersion);
        IsRemoteLocalizationChecking = true;
        RemoteLocalizationMessage = "Confronto con le versioni pubblicate…";

        try
        {
            var workspaceStatus = await _remoteLocalizationService.GetStatusAsync(project);
            if (!IsCurrentRemoteRequest(project, requestVersion))
            {
                return;
            }

            RemoteLocalizationFiles.Clear();
            foreach (var file in workspaceStatus.Files)
            {
                RemoteLocalizationFiles.Add(new RemoteLocalizationFileViewModel(file));
            }

            UpdateRemoteLocalizationViews(DateTimeOffset.Now);
            OnPropertyChanged(nameof(HasRemoteLocalizationFiles));
            RemoteLocalizationMessage = BuildRemoteLocalizationSummary(workspaceStatus.Files);
        }
        catch (RemoteLocalizationException exception)
        {
            if (IsCurrentRemoteRequest(project, requestVersion))
            {
                RemoteLocalizationFiles.Clear();
                _remoteLocalizationDocument.Reset();
                RemoteLocalizationRecentProblems.Clear();
                OnPropertyChanged(nameof(HasRemoteLocalizationRecentProblems));
                OnPropertyChanged(nameof(HasRemoteLocalizationFiles));
                RemoteLocalizationMessage = exception.Message;
            }
        }
        finally
        {
            if (IsCurrentRemoteRequest(project, requestVersion))
            {
                IsRemoteLocalizationChecking = false;
            }

            NotifyAuthenticationPropertiesChanged();
        }
    }

    public async Task<PublishedLocalizationComparison?> ComparePublishedLocalizationAsync(
        RemoteLocalizationFileViewModel file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var project = CurrentProject;
        if (project?.IsZiapInitialized != true || !file.CanSynchronize)
        {
            return null;
        }

        ClearError();
        IsRemoteLocalizationChecking = true;
        try
        {
            var comparison = await _publishedLocalizationSyncService.CompareAsync(
                project,
                file.Status);
            if (CurrentProject?.Id.Equals(project.Id, StringComparison.OrdinalIgnoreCase) != true)
            {
                return null;
            }
            StatusMessage =
                $"Confrontata in memoria la versione {comparison.VersionId} di {file.DisplayName}.";
            return comparison;
        }
        catch (Exception exception) when (exception is
            RemoteLocalizationException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
            return null;
        }
        finally
        {
            IsRemoteLocalizationChecking = false;
        }
    }

    public async Task<bool> SynchronizePublishedLocalizationAsync(
        RemoteLocalizationFileViewModel file,
        PublishedLocalizationComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(comparison);
        var project = CurrentProject;
        if (project?.IsZiapInitialized != true || !file.CanSynchronize)
        {
            return false;
        }

        ClearError();
        IsRemoteLocalizationChecking = true;
        try
        {
            var result = await _publishedLocalizationSyncService.SynchronizeAsync(
                project,
                file.Status,
                comparison);
            StatusMessage = result.Created
                ? $"Scaricata {file.DisplayName}, versione {result.VersionId}."
                : $"Aggiornata {file.DisplayName} alla versione {result.VersionId}.";
            return true;
        }
        catch (Exception exception) when (exception is
            RemoteLocalizationException or ExternalDocumentModificationException or
            IOException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
            return false;
        }
        finally
        {
            IsRemoteLocalizationChecking = false;
        }
    }

    public void OpenRemoteLocalizationInConsole(RemoteLocalizationFileViewModel file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (CurrentProject is null)
        {
            return;
        }

        ClearError();
        try
        {
            _consoleIntegrationService.Open(new ConsoleNavigationTarget(
                ProjectId: CurrentProject.Id,
                Area: ConsoleNavigationArea.Localization,
                Language: file.Locale,
                File: file.File));
            StatusMessage = $"Aperta ZIAP Console su {file.Locale}/{file.File}.";
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                NotSupportedException or System.ComponentModel.Win32Exception)
        {
            ErrorMessage = "Impossibile aprire la risorsa in ZIAP Console.";
        }
    }

    public void OpenRemoteLocalizationDocument()
    {
        if (CurrentProject?.IsZiapInitialized != true)
        {
            return;
        }

        var openDocument = OpenDocuments.FirstOrDefault(document =>
            document.IsRemoteLocalization);
        if (openDocument is null)
        {
            openDocument = DocumentTabViewModel.CreateRemoteLocalization(
                CurrentProject.Id,
                _remoteLocalizationDocument);
            OpenDocuments.Add(openDocument);
        }

        SelectedDocument = openDocument;
    }

    public void OpenPreflightDocument()
    {
        if (CurrentProject?.IsZiapInitialized != true)
        {
            return;
        }

        var openDocument = OpenDocuments.FirstOrDefault(document => document.IsPreflight);
        if (openDocument is null)
        {
            openDocument = DocumentTabViewModel.CreatePreflight(
                CurrentProject.Id,
                _preflightDocument);
            OpenDocuments.Add(openDocument);
        }

        SelectedDocument = openDocument;
    }

    public void AddFusionAudioEvent()
    {
        ActiveFusionAudioDocument?.AddEvent();
        NotifyEditingPropertiesChanged();
    }

    public void RemoveFusionAudioEvent()
    {
        if (ActiveFusionAudioDocument?.RemoveSelectedEvent() == true)
        {
            NotifyEditingPropertiesChanged();
        }
    }

    public void AddFusionAudioVariant()
    {
        ActiveFusionAudioDocument?.SelectedEntry?.AddVariant();
        NotifyEditingPropertiesChanged();
    }

    public void RemoveFusionAudioVariant(FusionAudioVariantViewModel variant)
    {
        ActiveFusionAudioDocument?.SelectedEntry?.RemoveVariant(variant);
        NotifyEditingPropertiesChanged();
    }

    public async Task ValidateFusionAudioAsync()
    {
        if (CurrentProject is not { } project || ActiveFusionAudioDocument is not { } document)
        {
            return;
        }

        try
        {
            if (document.IdentityError is { } identityError)
            {
                ErrorMessage = identityError;
                return;
            }
            var analysis = await _fusionAudioCatalogService.AnalyzeAsync(
                project,
                document.BuildCatalog());
            document.UpdateAnalysis(analysis.Assets, analysis.Diagnostics);
            StatusMessage = document.HasErrors
                ? "Fusion Audio contiene errori da correggere."
                : "Catalogo Fusion Audio valido.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ErrorMessage = $"Validazione Fusion Audio non riuscita: {exception.Message}";
        }
    }

    public async Task PlaySelectedFusionAudioAsync()
    {
        if (CurrentProject is not { } project ||
            ActiveFusionAudioDocument is not { } document ||
            document.SelectedEntry is not { } entry)
        {
            return;
        }

        ClearError();
        try
        {
            var resolution = await _fusionAudioPlaybackResolver.ResolveAsync(
                project,
                document.BuildCatalog(),
                entry.EventId,
                entry.ToModel());
            if (resolution.Status == FusionAudioPlaybackResolutionStatus.Cooldown)
            {
                StatusMessage = $"Preview evento soppressa · cooldown " +
                    $"{resolution.RemainingCooldownMs} ms";
                return;
            }
            if (resolution.Plan is not { } plan)
            {
                ErrorMessage = resolution.Message ??
                    "L'evento selezionato non può essere risolto.";
                return;
            }

            _audioPreviewService.Play(plan);
            _fusionAudioPlaybackResolver.CommitPlayback(project, plan);
            document.RecordPlayback(plan);
            StatusMessage = $"Preview evento · {plan.RelativePath} · " +
                $"{plan.Volume:P0} · pitch {plan.Pitch:0.#}";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ErrorMessage = $"Impossibile riprodurre l'asset: {exception.Message}";
        }
    }

    public async Task PlayFusionAudioVariantAsync(FusionAudioVariantViewModel variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        if (CurrentProject is not { } project ||
            ActiveFusionAudioDocument?.SelectedEntry is not { } entry)
        {
            return;
        }

        ClearError();
        var entryModel = entry.ToModel();
        var model = entryModel with
        {
            Source = entryModel.Source with { Files = [variant.File] },
        };
        try
        {
            var asset = await _fusionAudioCatalogService.ResolvePreviewAsync(
                project,
                entry.EventId,
                model);
            if (asset is not { Exists: true, ResolvedPath: not null })
            {
                ErrorMessage = "L'asset selezionato non esiste o non può essere risolto.";
                return;
            }

            _audioPreviewService.PlayRaw(asset.ResolvedPath);
            StatusMessage = $"Preview asset grezzo · {asset.RelativePath}";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ErrorMessage = $"Impossibile riprodurre l'asset: {exception.Message}";
        }
    }

    public void PlayFusionAudioFile(FusionAudioFileOptionViewModel option)
    {
        ArgumentNullException.ThrowIfNull(option);
        ClearError();
        try
        {
            _audioPreviewService.PlayRaw(option.ResolvedPath);
            StatusMessage = $"Preview asset grezzo · audio/se/{option.CatalogPath}";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ErrorMessage = $"Impossibile riprodurre l'asset: {exception.Message}";
        }
    }

    public void StopFusionAudioPreview()
    {
        _audioPreviewService.Stop();
        StatusMessage = "Preview audio arrestata.";
    }

    public void OpenFusionAudioFolder()
    {
        if (CurrentProject is null) return;
        var path = Path.Combine(CurrentProject.Path, "audio", "se");
        try
        {
            _shellService.OpenFolder(path);
        }
        catch (Exception exception) when (
            exception is DirectoryNotFoundException or System.ComponentModel.Win32Exception)
        {
            ErrorMessage = $"Impossibile aprire la cartella audio: {exception.Message}";
        }
    }

    public async Task AnalyzePreflightAsync()
    {
        var project = CurrentProject;
        if (project?.IsZiapInitialized != true || IsPreflightScanning)
        {
            return;
        }

        IsPreflightScanning = true;
        try
        {
            var suppressions = await _preflightSuppressionStore.LoadAsync(project.Path);
            if (!ReferenceEquals(project, CurrentProject))
            {
                return;
            }

            _preflightResult = await _preflightScanner.ScanAsync(project, suppressions);
            if (!ReferenceEquals(project, CurrentProject))
            {
                return;
            }

            ApplyPreflightResult();
            StatusMessage = "Pre-Flight completato · " +
                $"{FormatCount(_preflightResult.ErrorCount, "errore", "errori")}, " +
                $"{FormatCount(_preflightResult.WarningCount, "avviso", "avvisi")}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or ExternalDocumentModificationException)
        {
            ErrorMessage = $"Pre-Flight non completato: {exception.Message}";
        }
        finally
        {
            IsPreflightScanning = false;
        }
    }

    public async Task IgnorePreflightIssueAsync(
        PreflightIssueViewModel item,
        string? reason)
    {
        if (CurrentProject?.IsZiapInitialized != true || !item.CanIgnore)
        {
            return;
        }

        var projectPath = CurrentProject.Path;
        await PersistPreflightSuppressionChangeAsync(() =>
            _preflightSuppressionStore.IgnoreAsync(
            projectPath,
            new PreflightSuppression
            {
                RuleId = item.RuleId,
                Scope = item.Scope,
                RecordId = item.RecordId,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                IgnoredAt = DateTimeOffset.Now,
            }));
    }

    public async Task RestorePreflightIssueAsync(PreflightIssueViewModel item)
    {
        if (CurrentProject?.IsZiapInitialized != true || !item.CanRestore)
        {
            return;
        }

        var projectPath = CurrentProject.Path;
        await PersistPreflightSuppressionChangeAsync(() =>
            _preflightSuppressionStore.RestoreAsync(projectPath, item.Identity));
    }

    public async Task RestoreAllPreflightIssuesAsync()
    {
        if (CurrentProject?.IsZiapInitialized != true)
        {
            return;
        }

        var projectPath = CurrentProject.Path;
        await PersistPreflightSuppressionChangeAsync(() =>
            _preflightSuppressionStore.RestoreAllAsync(projectPath));
    }

    public async Task CleanObsoletePreflightSuppressionsAsync()
    {
        if (CurrentProject?.IsZiapInitialized != true ||
            _preflightResult.ObsoleteSuppressions.Count == 0)
        {
            return;
        }

        var projectPath = CurrentProject.Path;
        var obsolete = _preflightResult.ObsoleteSuppressions;
        await PersistPreflightSuppressionChangeAsync(() =>
            _preflightSuppressionStore.RemoveObsoleteAsync(projectPath, obsolete));
    }

    public async Task OpenPreflightIssueAsync(PreflightIssueViewModel item)
    {
        if (item.NavigationTarget is null)
        {
            return;
        }

        await NavigateToReferenceAsync(item.NavigationTarget.AbsoluteUri);
        if (SelectedDocument?.Database?.AdvancedEditor is { } advancedEditor)
        {
            advancedEditor.IsExpanded = true;
        }
    }

    private async Task PersistPreflightSuppressionChangeAsync(Func<Task> mutation)
    {
        ClearError();
        try
        {
            await mutation();
            await AnalyzePreflightAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or ExternalDocumentModificationException or InvalidOperationException)
        {
            ErrorMessage = $"Impossibile aggiornare le eccezioni Pre-Flight: {exception.Message}";
        }
    }

    public void OpenRemoteLocalizationAreaInConsole()
    {
        if (CurrentProject is null)
        {
            return;
        }

        ClearError();
        try
        {
            _consoleIntegrationService.Open(new ConsoleNavigationTarget(
                ProjectId: CurrentProject.Id,
                Area: ConsoleNavigationArea.Localization));
            StatusMessage = "Aperta l'area Localization di ZIAP Console.";
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                NotSupportedException or System.ComponentModel.Win32Exception)
        {
            ErrorMessage = "Impossibile aprire l'area Localization in ZIAP Console.";
        }
    }

    public async Task OpenDocumentAsync(ProjectExplorerItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (CurrentProject is null || item.Document is null)
        {
            return;
        }

        var openDocument = OpenDocuments.FirstOrDefault(
            document => document.Descriptor.Id == item.Document.Id);
        if (openDocument is not null)
        {
            SelectedDocument = openDocument;
            return;
        }

        ClearError();
        IsBusy = true;

        try
        {
            var document = await _documentService.OpenAsync(CurrentProject, item.Document);
            DocumentTabViewModel tab;
            if (document is RpgMakerDatabaseDocument databaseDocument)
            {
                var assetPreviews = await _assetPreviewService.CreatePreviewsAsync(
                    CurrentProject,
                    databaseDocument);
                var editSession = _editSessionFactory.Create(databaseDocument, assetPreviews);
                tab = DocumentTabViewModel.Create(databaseDocument, assetPreviews, editSession);
                tab.Database?.ApplyPreflightIssues(_preflightResult.ActiveIssues);
            }
            else if (document is FusionAudioDocument fusionAudioDocument)
            {
                var fusionAudio = new FusionAudioDocumentViewModel(fusionAudioDocument);
                fusionAudio.UpdateAnalysis(
                    fusionAudioDocument.Assets,
                    fusionAudioDocument.Diagnostics);
                tab = DocumentTabViewModel.CreateFusionAudio(fusionAudioDocument, fusionAudio);
            }
            else if (document is FusionBossWorkspaceDocument fusionBossDocument)
            {
                var editSession = fusionBossDocument is
                    { EncounterSourceRoot: not null, EncounterSourceSnapshot: not null }
                        ? new FusionBossEditSession(fusionBossDocument)
                        : null;
                tab = DocumentTabViewModel.CreateFusionBoss(
                    fusionBossDocument,
                    new FusionBossDocumentViewModel(fusionBossDocument, editSession),
                    editSession);
            }
            else
            {
                throw new DocumentLoadException(
                    $"Il documento '{item.Document.DisplayName}' non ha una vista disponibile.");
            }
            tab.PropertyChanged += DocumentTab_PropertyChanged;
            OpenDocuments.Add(tab);
            SelectedDocument = tab;
        }
        catch (DocumentLoadException exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ShowProjectOverview()
    {
        var overview = OpenDocuments.FirstOrDefault(document => document.IsProjectOverview);
        if (overview is not null)
        {
            SelectedDocument = overview;
        }
    }

    public bool CloseDocument(
        DocumentTabViewModel document,
        bool discardChanges = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.IsClosable || document.IsDirty && !discardChanges)
        {
            return false;
        }

        var wasSelected = ReferenceEquals(document, SelectedDocument);
        OpenDocuments.Remove(document);
        document.PropertyChanged -= DocumentTab_PropertyChanged;
        document.Dispose();
        if (wasSelected)
        {
            ShowProjectOverview();
        }

        NotifyEditingPropertiesChanged();
        return true;
    }

    public void CloseExternalSurfaces()
    {
        foreach (var document in OpenDocuments)
        {
            document.FusionBoss?.CloseExternalSurfaces();
        }
    }

    public async Task<DocumentSaveResult?> SaveSelectedDocumentAsync() =>
        SelectedDocument is null
            ? null
            : await SaveDocumentAsync(SelectedDocument);

    public async Task<DocumentSaveResult?> SaveDocumentAsync(DocumentTabViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (IsBusy || !document.IsDirty)
        {
            return null;
        }

        ClearError();
        IsBusy = true;
        try
        {
            DocumentSaveResult result;
            if (document.FusionAudio is { } fusionAudio && CurrentProject is { } project)
            {
                result = await SaveFusionAudioAsync(project, fusionAudio);
            }
            else if (document.EditSession is { } editSession)
            {
                result = await _documentSaveService.SaveAsync(editSession);
            }
            else if (document.FusionBossEditSession is { } fusionBossEditSession)
            {
                result = await _fusionBossAuthoringService.SaveAsync(fusionBossEditSession);
            }
            else
            {
                return null;
            }
            ApplySaveResult(document, result);
            if (result.Status == DocumentSaveStatus.Saved)
            {
                if (document.FusionBossEditSession is not null)
                {
                    await ReloadFusionBossDocumentAsync(document);
                }
                await AnalyzePreflightAsync();
            }
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<DocumentSaveResult> SaveFusionAudioAsync(
        ZiapProject project,
        FusionAudioDocumentViewModel fusionAudio)
    {
        try
        {
            if (fusionAudio.IdentityError is { } identityError)
            {
                return new DocumentSaveResult
                {
                    Status = DocumentSaveStatus.ValidationFailed,
                    Message = identityError,
                };
            }
            var catalog = fusionAudio.BuildCatalog();
            var analysis = await _fusionAudioCatalogService.AnalyzeAsync(project, catalog);
            fusionAudio.UpdateAnalysis(analysis.Assets, analysis.Diagnostics);
            if (fusionAudio.HasErrors)
            {
                return new DocumentSaveResult
                {
                    Status = DocumentSaveStatus.ValidationFailed,
                    Message = "Il catalogo Fusion Audio contiene errori.",
                };
            }

            var snapshot = await _fusionAudioCatalogService.SaveAsync(
                project,
                catalog,
                fusionAudio.SourceSnapshot);
            fusionAudio.AcceptSaved(snapshot);
            return new DocumentSaveResult { Status = DocumentSaveStatus.Saved };
        }
        catch (ExternalDocumentModificationException exception)
        {
            return new DocumentSaveResult
            {
                Status = DocumentSaveStatus.ExternalModification,
                Message = exception.Message,
                Exception = exception,
            };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Text.Json.JsonException or InvalidOperationException)
        {
            return new DocumentSaveResult
            {
                Status = DocumentSaveStatus.Failed,
                Message = exception.Message,
                Exception = exception,
            };
        }
    }

    public async Task<bool> SaveAllAsync()
    {
        if (IsBusy)
        {
            return false;
        }

        var dirtyDocuments = DirtyDocuments;
        foreach (var document in dirtyDocuments)
        {
            var result = await SaveDocumentAsync(document);
            if (result is not null && !result.IsSuccess)
            {
                SelectedDocument = document;
                return false;
            }
        }

        return true;
    }

    public void UndoSelectedDocument()
    {
        if (SelectedDocument?.EditSession?.Undo() == true ||
            SelectedDocument?.FusionBossEditSession?.Undo() == true)
        {
            StatusMessage = $"Annullata l'ultima modifica in {SelectedDocument.DisplayName}.";
        }
    }

    public void RedoSelectedDocument()
    {
        if (SelectedDocument?.EditSession?.Redo() == true ||
            SelectedDocument?.FusionBossEditSession?.Redo() == true)
        {
            StatusMessage = $"Ripristinata l'ultima modifica in {SelectedDocument.DisplayName}.";
        }
    }

    public async Task NavigateToReferenceAsync(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var targetUri))
        {
            return;
        }

        if (targetUri.Scheme.Equals("fusionaudio", StringComparison.OrdinalIgnoreCase) &&
            targetUri.Host.Equals("catalog", StringComparison.OrdinalIgnoreCase))
        {
            var resourceId = new Uri("fusionaudio://catalog");
            var fusionExplorerItem = FindExplorerItem(ProjectExplorerNodes, resourceId);
            if (fusionExplorerItem is null)
            {
                ErrorMessage = "L'integrazione Fusion Audio non è disponibile nel Project Explorer.";
                return;
            }
            await OpenDocumentAsync(fusionExplorerItem);
            var fusionSegments = targetUri.AbsolutePath.Trim('/').Split('/');
            if (fusionSegments.Length == 2 &&
                fusionSegments[0].Equals("event", StringComparison.OrdinalIgnoreCase))
            {
                ActiveFusionAudioDocument?.SelectEvent(Uri.UnescapeDataString(fusionSegments[1]));
            }
            return;
        }

        if (targetUri.Scheme.Equals("fusionboss", StringComparison.OrdinalIgnoreCase) &&
            targetUri.Host.Equals("workspace", StringComparison.OrdinalIgnoreCase))
        {
            var resourceId = new Uri("fusionboss://workspace/");
            var bossExplorerItem = FindExplorerItem(ProjectExplorerNodes, resourceId);
            if (bossExplorerItem is null)
            {
                ErrorMessage = "L'integrazione Fusion Boss Battle non è disponibile nel Project Explorer.";
                return;
            }
            await OpenDocumentAsync(bossExplorerItem);
            ActiveFusionBossDocument?.NavigateTo(targetUri);
            return;
        }

        if (!targetUri.Scheme.Equals("rpgmaker", StringComparison.OrdinalIgnoreCase) ||
            !targetUri.Host.Equals("database", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var segments = targetUri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length != 2 || !int.TryParse(segments[1], out var entryId))
        {
            return;
        }

        var documentResourceId = new Uri($"rpgmaker://database/{segments[0]}");
        var explorerItem = FindExplorerItem(ProjectExplorerNodes, documentResourceId);
        if (explorerItem is null)
        {
            ErrorMessage = $"La risorsa '{documentResourceId}' non è presente nel Project Explorer.";
            return;
        }

        await OpenDocumentAsync(explorerItem);
        if (SelectedDocument?.Descriptor.ResourceId == documentResourceId)
        {
            SelectedDocument.Database?.SelectEntryById(entryId);
        }
    }

    private async Task LoadProjectAsync(string path)
    {
        IsBusy = true;
        var loaded = false;

        try
        {
            var project = await _projectService.LoadAsync(path);
            CurrentProject = project;
            ResetDocumentWorkspace(project);
            await RefreshProjectExplorerAsync(project);
            StatusMessage = $"{project.ProjectTypeDisplayName} • {project.Path}";
            loaded = true;

            try
            {
                ReplaceRecentProjects(await _recentProjectService.AddAsync(project));
            }
            catch (SettingsException exception)
            {
                ErrorMessage = exception.Message;
            }
        }
        catch (ProjectLoadException exception)
        {
            ErrorMessage = exception.Message;
            StatusMessage = "Il progetto non è stato caricato.";
        }
        finally
        {
            IsBusy = false;
        }

        if (loaded)
        {
            await AnalyzePreflightAsync();
            await RefreshRemoteLocalizationAsync();
        }
    }

    private void ReplaceRecentProjects(IEnumerable<ZiapProject> projects)
    {
        RecentProjects.Clear();
        foreach (var project in projects)
        {
            RecentProjects.Add(project);
        }
    }

    private async Task RefreshProjectExplorerAsync(ZiapProject project)
    {
        ProjectExplorerNodes.Clear();

        try
        {
            var nodes = await _projectProviderService.BuildExplorerAsync(project);
            foreach (var node in nodes)
            {
                ProjectExplorerNodes.Add(ProjectExplorerItemViewModel.FromRoot(node));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = "Il progetto è aperto, ma non è stato possibile costruire il Project Explorer.";
        }
    }

    private void ResetDocumentWorkspace(ZiapProject project)
    {
        _audioPreviewService.Stop();
        foreach (var document in OpenDocuments)
        {
            document.PropertyChanged -= DocumentTab_PropertyChanged;
            document.Dispose();
        }

        OpenDocuments.Clear();
        _remoteLocalizationDocument.Reset();
        _preflightResult = PreflightScanResult.Empty;
        _preflightDocument.Reset();
        RemoteLocalizationRecentProblems.Clear();
        var overview = DocumentTabViewModel.CreateProjectOverview(project.Id);
        OpenDocuments.Add(overview);
        SelectedDocument = overview;
        ResetRemoteLocalizationStatus(project.IsZiapInitialized
            ? "Verifica delle versioni pubblicate in attesa…"
            : "Inizializza il progetto ZIAP per verificare le versioni pubblicate.");
    }

    private void ApplyPreflightResult()
    {
        _preflightDocument.Update(_preflightResult);
        foreach (var document in OpenDocuments)
        {
            document.Database?.ApplyPreflightIssues(_preflightResult.ActiveIssues);
        }

        OnPropertyChanged(nameof(PreflightDocument));
    }

    private void DocumentTab_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DocumentTabViewModel.IsDirty) or
            nameof(DocumentTabViewModel.CanUndo) or
            nameof(DocumentTabViewModel.CanRedo))
        {
            NotifyEditingPropertiesChanged();
        }
        if (args.PropertyName == nameof(DocumentTabViewModel.FusionBoss))
        {
            OnPropertyChanged(nameof(ActiveFusionBossDocument));
        }
    }

    private async Task ReloadFusionBossDocumentAsync(DocumentTabViewModel tab)
    {
        if (CurrentProject is null)
        {
            return;
        }
        var previousEncounterId = tab.FusionBoss?.SelectedEncounter?.Id;
        var previousPhaseId = tab.FusionBoss?.SelectedPhase?.Id;
        var previousSequenceId = tab.FusionBoss?.SelectedSequence?.Id;
        var loaded = await _documentService.OpenAsync(CurrentProject, tab.Descriptor);
        if (loaded is not FusionBossWorkspaceDocument document)
        {
            throw new DocumentLoadException("Impossibile ricaricare Fusion Boss Battle dopo il salvataggio.");
        }
        var session = new FusionBossEditSession(document);
        var viewModel = new FusionBossDocumentViewModel(document, session);
        if (previousEncounterId is not null)
        {
            viewModel.SelectedEncounter = viewModel.Encounters.FirstOrDefault(encounter =>
                encounter.Id.Equals(previousEncounterId, StringComparison.OrdinalIgnoreCase)) ??
                viewModel.SelectedEncounter;
        }
        if (previousPhaseId is not null)
        {
            viewModel.SelectedPhase = viewModel.Phases.FirstOrDefault(phase =>
                phase.Id.Equals(previousPhaseId, StringComparison.OrdinalIgnoreCase)) ??
                viewModel.SelectedPhase;
        }
        if (previousSequenceId is not null)
        {
            viewModel.SelectedSequence = viewModel.Sequences.FirstOrDefault(sequence =>
                sequence.Id.Equals(previousSequenceId, StringComparison.OrdinalIgnoreCase)) ??
                viewModel.SelectedSequence;
        }
        tab.ReplaceFusionBoss(viewModel, session);
        OnPropertyChanged(nameof(ActiveFusionBossDocument));
        NotifyEditingPropertiesChanged();
    }

    private void ApplySaveResult(
        DocumentTabViewModel document,
        DocumentSaveResult result)
    {
        switch (result.Status)
        {
            case DocumentSaveStatus.Saved:
                var warningCount = result.Validation.Issues.Count(issue =>
                    issue.Severity == DocumentValidationSeverity.Warning);
                StatusMessage = warningCount == 0
                    ? $"{document.DisplayName} salvato."
                    : $"{document.DisplayName} salvato con {warningCount} avvisi.";
                break;
            case DocumentSaveStatus.ValidationFailed:
                ErrorMessage = result.Validation.Issues.FirstOrDefault(issue =>
                    issue.Severity == DocumentValidationSeverity.Error)?.Message ??
                    result.Message;
                break;
            case DocumentSaveStatus.ExternalModification:
                ErrorMessage = $"{result.Message} Salvataggio annullato per proteggere la versione più recente.";
                break;
            case DocumentSaveStatus.Failed:
                ErrorMessage = result.Message;
                break;
        }
    }

    private void NotifyEditingPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanSaveDocument));
        OnPropertyChanged(nameof(CanSaveAll));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(DirtyDocuments));
    }

    private static ProjectExplorerItemViewModel? FindExplorerItem(
        IEnumerable<ProjectExplorerItemViewModel> items,
        Uri resourceId)
    {
        foreach (var item in items)
        {
            if (item.Document?.ResourceId == resourceId)
            {
                return item;
            }

            var child = FindExplorerItem(item.Children, resourceId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private void NotifyProjectPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(HasNoProject));
        OnPropertyChanged(nameof(NeedsInitialization));
        OnPropertyChanged(nameof(CanInitialize));
        OnPropertyChanged(nameof(ProjectStateLabel));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectExplorerTitle));
        OnPropertyChanged(nameof(PackageName));
        OnPropertyChanged(nameof(ProjectVersion));
        OnPropertyChanged(nameof(ProjectType));
        OnPropertyChanged(nameof(Publisher));
        OnPropertyChanged(nameof(ProjectPath));
        OnPropertyChanged(nameof(HasRemoteLocalizationCard));
        OnPropertyChanged(nameof(HasPreflightCard));
        OnPropertyChanged(nameof(CanRefreshRemoteLocalization));
        OnPropertyChanged(nameof(CanAnalyzePreflight));
    }

    private void NotifyAuthenticationPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(IsNotAuthenticated));
        OnPropertyChanged(nameof(AccountDisplayName));
        OnPropertyChanged(nameof(AccountDetail));
        OnPropertyChanged(nameof(CanSignIn));
        OnPropertyChanged(nameof(CanSignOut));
    }

    private void ResetRemoteLocalizationStatus(string message)
    {
        Interlocked.Increment(ref _remoteLocalizationRequestVersion);
        RemoteLocalizationFiles.Clear();
        _remoteLocalizationDocument.Reset();
        RemoteLocalizationRecentProblems.Clear();
        OnPropertyChanged(nameof(HasRemoteLocalizationFiles));
        RemoteLocalizationMessage = message;
        IsRemoteLocalizationChecking = false;
    }

    private bool IsCurrentRemoteRequest(ZiapProject project, int requestVersion) =>
        requestVersion == _remoteLocalizationRequestVersion &&
        CurrentProject?.Id.Equals(project.Id, StringComparison.OrdinalIgnoreCase) == true &&
        CurrentProject.Path.Equals(project.Path, StringComparison.OrdinalIgnoreCase);

    private static string BuildRemoteLocalizationSummary(
        IReadOnlyList<RemoteLocalizationFileStatus> files)
    {
        if (files.Count == 0)
        {
            return "Nessun file Localization locale o pubblicato trovato.";
        }

        var aligned = files.Count(file => file.Alignment == RemoteLocalizationAlignment.Aligned);
        var different = files.Count(file => file.Alignment == RemoteLocalizationAlignment.Different);
        var onlyLocal = files.Count(file =>
            file.Alignment == RemoteLocalizationAlignment.MissingRemote);
        var onlyPublished = files.Count(file =>
            file.Alignment == RemoteLocalizationAlignment.MissingLocal);
        var unresolved = files.Count - aligned - different - onlyLocal - onlyPublished;
        var parts = new List<string> { $"{aligned} allineati" };
        if (different > 0) parts.Add($"{different} differenti");
        if (onlyLocal > 0) parts.Add($"{onlyLocal} solo locali");
        if (onlyPublished > 0) parts.Add($"{onlyPublished} solo remoti");
        if (unresolved > 0) parts.Add($"{unresolved} non verificabili");
        return string.Join(" · ", parts);
    }

    private void UpdateRemoteLocalizationViews(DateTimeOffset checkedAt)
    {
        _remoteLocalizationDocument.Update(RemoteLocalizationFiles, checkedAt);
        RemoteLocalizationRecentProblems.Clear();
        foreach (var file in RemoteLocalizationFiles
                     .Where(file => file.Status.Alignment != RemoteLocalizationAlignment.Aligned)
                     .OrderBy(file => file.Status.Alignment == RemoteLocalizationAlignment.Different ? 0 : 1)
                     .ThenBy(file => file.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .Take(3))
        {
            RemoteLocalizationRecentProblems.Add(file);
        }
        OnPropertyChanged(nameof(HasRemoteLocalizationRecentProblems));
    }

    public bool HasRemoteLocalizationRecentProblems =>
        RemoteLocalizationRecentProblems.Count > 0;

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

    private static string FormatCount(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
