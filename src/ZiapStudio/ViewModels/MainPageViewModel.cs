using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;
using ZiapStudio.Platform.Windows;
using ZiapStudio.Services;
using ZiapStudio.Services.Assets;
using ZiapStudio.Services.Authentication;
using ZiapStudio.Services.Documents;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Initialization;
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
    private readonly IRecentProjectService _recentProjectService;
    private readonly WindowsFolderPickerService _folderPickerService;
    private readonly WindowsShellService _shellService;
    private readonly ConsoleIntegrationService _consoleIntegrationService;
    private readonly RemoteLocalizationService _remoteLocalizationService;
    private readonly PublishedLocalizationSyncService _publishedLocalizationSyncService;
    private readonly ZiapAuthenticationService _authenticationService;
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

    public MainPageViewModel(
        ProjectService projectService,
        ProjectInitializationService projectInitializationService,
        ProjectIdGenerator projectIdGenerator,
        ProjectProviderService projectProviderService,
        DocumentService documentService,
        AssetPreviewService assetPreviewService,
        DocumentEditSessionFactory editSessionFactory,
        DocumentSaveService documentSaveService,
        IRecentProjectService recentProjectService,
        WindowsFolderPickerService folderPickerService,
        WindowsShellService shellService,
        ConsoleIntegrationService consoleIntegrationService,
        RemoteLocalizationService remoteLocalizationService,
        PublishedLocalizationSyncService publishedLocalizationSyncService,
        ZiapAuthenticationService authenticationService)
    {
        _projectService = projectService;
        _projectInitializationService = projectInitializationService;
        _projectIdGenerator = projectIdGenerator;
        _projectProviderService = projectProviderService;
        _documentService = documentService;
        _assetPreviewService = assetPreviewService;
        _editSessionFactory = editSessionFactory;
        _documentSaveService = documentSaveService;
        _recentProjectService = recentProjectService;
        _folderPickerService = folderPickerService;
        _shellService = shellService;
        _consoleIntegrationService = consoleIntegrationService;
        _remoteLocalizationService = remoteLocalizationService;
        _publishedLocalizationSyncService = publishedLocalizationSyncService;
        _authenticationService = authenticationService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ZiapProject> RecentProjects { get; } = [];

    public ObservableCollection<ProjectExplorerItemViewModel> ProjectExplorerNodes { get; } = [];

    public ObservableCollection<DocumentTabViewModel> OpenDocuments { get; } = [];

    public ObservableCollection<RemoteLocalizationFileViewModel> RemoteLocalizationFiles { get; } = [];

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
                OnPropertyChanged(nameof(IsDatabaseDocumentSelected));
                OnPropertyChanged(nameof(ActiveDatabaseDocument));
                NotifyEditingPropertiesChanged();
            }
        }
    }

    public bool IsProjectOverviewSelected => SelectedDocument?.IsProjectOverview == true;

    public bool IsDatabaseDocumentSelected => ActiveDatabaseDocument is not null;

    public RpgMakerDatabaseDocumentViewModel? ActiveDatabaseDocument => SelectedDocument?.Database;

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

            OnPropertyChanged(nameof(HasRemoteLocalizationFiles));
            RemoteLocalizationMessage = BuildRemoteLocalizationSummary(workspaceStatus.Files);
        }
        catch (RemoteLocalizationException exception)
        {
            if (IsCurrentRemoteRequest(project, requestVersion))
            {
                RemoteLocalizationFiles.Clear();
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
            if (document is not RpgMakerDatabaseDocument databaseDocument)
            {
                throw new DocumentLoadException(
                    $"Il documento '{item.Document.DisplayName}' non ha una vista disponibile.");
            }

            var assetPreviews = await _assetPreviewService.CreatePreviewsAsync(
                CurrentProject,
                databaseDocument);
            var editSession = _editSessionFactory.Create(databaseDocument, assetPreviews);
            var tab = DocumentTabViewModel.Create(databaseDocument, assetPreviews, editSession);
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
        if (wasSelected)
        {
            ShowProjectOverview();
        }

        NotifyEditingPropertiesChanged();
        return true;
    }

    public async Task<DocumentSaveResult?> SaveSelectedDocumentAsync() =>
        SelectedDocument is null
            ? null
            : await SaveDocumentAsync(SelectedDocument);

    public async Task<DocumentSaveResult?> SaveDocumentAsync(DocumentTabViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (IsBusy || document.EditSession is null || !document.EditSession.IsDirty)
        {
            return null;
        }

        ClearError();
        IsBusy = true;
        try
        {
            var result = await _documentSaveService.SaveAsync(document.EditSession);
            ApplySaveResult(document, result);
            return result;
        }
        finally
        {
            IsBusy = false;
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
        if (SelectedDocument?.EditSession?.Undo() == true)
        {
            StatusMessage = $"Annullata l'ultima modifica in {SelectedDocument.DisplayName}.";
        }
    }

    public void RedoSelectedDocument()
    {
        if (SelectedDocument?.EditSession?.Redo() == true)
        {
            StatusMessage = $"Ripristinata l'ultima modifica in {SelectedDocument.DisplayName}.";
        }
    }

    public async Task NavigateToReferenceAsync(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var targetUri) ||
            !targetUri.Scheme.Equals("rpgmaker", StringComparison.OrdinalIgnoreCase) ||
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
        foreach (var document in OpenDocuments)
        {
            document.PropertyChanged -= DocumentTab_PropertyChanged;
        }

        OpenDocuments.Clear();
        var overview = DocumentTabViewModel.CreateProjectOverview(project.Id);
        OpenDocuments.Add(overview);
        SelectedDocument = overview;
        ResetRemoteLocalizationStatus(project.IsZiapInitialized
            ? "Verifica delle versioni pubblicate in attesa…"
            : "Inizializza il progetto ZIAP per verificare le versioni pubblicate.");
    }

    private void DocumentTab_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DocumentTabViewModel.IsDirty) or
            nameof(DocumentTabViewModel.CanUndo) or
            nameof(DocumentTabViewModel.CanRedo))
        {
            NotifyEditingPropertiesChanged();
        }
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
        OnPropertyChanged(nameof(CanRefreshRemoteLocalization));
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
        var missing = files.Count(file => file.Alignment is
            RemoteLocalizationAlignment.MissingLocal or RemoteLocalizationAlignment.MissingRemote);
        var unresolved = files.Count - aligned - different - missing;
        var parts = new List<string> { $"{aligned} allineati" };
        if (different > 0) parts.Add($"{different} differenti");
        if (missing > 0) parts.Add($"{missing} mancanti");
        if (unresolved > 0) parts.Add($"{unresolved} non verificabili");
        return string.Join(" · ", parts);
    }

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

    private static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
