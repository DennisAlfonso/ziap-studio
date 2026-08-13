using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ZiapStudio.Core.Models;
using ZiapStudio.Dialogs;
using ZiapStudio.Services;
using ZiapStudio.ViewModels;

namespace ZiapStudio;

public sealed partial class MainPage : Page
{
    private const double ExpandedProjectsPaneWidth = 292;
    private const double CompactProjectsPaneWidth = 64;
    private StudioLayoutSettings _layoutSettings;
    private double _explorerWidth;
    private double _databaseListWidth;
    private bool _isProjectPaneExpanded = true;
    private FrameworkElement? _activeSplitter;
    private ColumnDefinition? _activeSplitterColumn;
    private double _splitterStartPointerX;
    private double _splitterStartWidth;
    private double _splitterMinimumWidth;
    private double _splitterMaximumWidth;

    public MainPageViewModel ViewModel { get; }

    public MainPage(
        MainPageViewModel viewModel,
        StudioLayoutSettings layoutSettings)
    {
        ViewModel = viewModel;
        _layoutSettings = layoutSettings;
        _explorerWidth = layoutSettings.ExplorerWidth;
        _databaseListWidth = layoutSettings.DatabaseListWidth;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        SetProjectsPaneExpanded(expanded: true);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) =>
        await ViewModel.InitializeAsync();

    public StudioLayoutSettings CaptureLayoutSettings() =>
        _layoutSettings = _layoutSettings with
        {
            IsProjectPaneExpanded = ViewModel.HasProject && _isProjectPaneExpanded,
            ExplorerWidth = _explorerWidth,
            DatabaseListWidth = _databaseListWidth,
        };

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainPageViewModel.HasProject))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                SetProjectsPaneExpanded(
                    !ViewModel.HasProject || _layoutSettings.IsProjectPaneExpanded);
                ApplyPersistedPanelWidths();
            });
            return;
        }

        if (args.PropertyName == nameof(MainPageViewModel.HasRemoteLocalizationCard))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (OverviewSections is not null)
                {
                    ApplyOverviewSectionsLayout(OverviewSections.ActualWidth);
                }
            });
        }
    }

    private void ToggleProjectsPane_Click(object sender, RoutedEventArgs e) =>
        SetProjectsPaneExpanded(!ViewModel.HasProject || !_isProjectPaneExpanded);

    private void SetProjectsPaneExpanded(bool expanded)
    {
        _isProjectPaneExpanded = expanded;
        ProjectsPaneColumn.Width = new GridLength(
            expanded ? ExpandedProjectsPaneWidth : CompactProjectsPaneWidth);
        ExpandedProjectsPane.Visibility = expanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompactProjectsPane.Visibility = expanded
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ProjectWorkspace_Loaded(object sender, RoutedEventArgs e) =>
        ApplyPersistedPanelWidths();

    private void DatabaseDocument_Loaded(object sender, RoutedEventArgs e) =>
        DatabaseListColumn.Width = new GridLength(_databaseListWidth);

    private void ApplyPersistedPanelWidths()
    {
        if (ProjectExplorerColumn is not null)
        {
            ProjectExplorerColumn.Width = new GridLength(_explorerWidth);
        }

        if (DatabaseListColumn is not null)
        {
            DatabaseListColumn.Width = new GridLength(_databaseListWidth);
        }
    }

    private void ProjectExplorerPane_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width >= 220)
        {
            _explorerWidth = e.NewSize.Width;
        }
    }

    private void DatabaseListPane_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width >= 360)
        {
            _databaseListWidth = e.NewSize.Width;
        }
    }

    private void OverviewSections_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyOverviewSectionsLayout(e.NewSize.Width);

    private void ApplyOverviewSectionsLayout(double availableWidth)
    {
        var useWideLayout = availableWidth >= 1050 &&
            ViewModel.HasRemoteLocalizationCard;
        OverviewPrimaryColumn.Width = new GridLength(
            useWideLayout ? 2 : 1,
            GridUnitType.Star);
        OverviewSectionGap.Width = new GridLength(useWideLayout ? 24 : 0);
        OverviewSecondaryColumn.Width = useWideLayout
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        if (RemoteLocalizationCard is not null)
        {
            Grid.SetColumnSpan(RemoteLocalizationCard, useWideLayout ? 1 : 3);
        }
        Grid.SetRow(OverviewDetailsSection, useWideLayout ? 0 : 1);
        Grid.SetColumn(OverviewDetailsSection, useWideLayout ? 2 : 0);
        Grid.SetColumnSpan(OverviewDetailsSection, useWideLayout ? 1 : 3);
    }

    private void ExplorerSplitter_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        BeginColumnResize(
            sender,
            e,
            ProjectExplorerColumn,
            minimumWidth: 220,
            maximumWidth: 550);

    private void ExplorerSplitter_PointerMoved(object sender, PointerRoutedEventArgs e) =>
        ContinueColumnResize(sender, e);

    private void DatabaseSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var availableMaximum = Math.Max(
            360,
            DatabaseDocument.ActualWidth - 306);
        BeginColumnResize(
            sender,
            e,
            DatabaseListColumn,
            minimumWidth: 360,
            maximumWidth: Math.Min(1200, availableMaximum));
    }

    private void DatabaseSplitter_PointerMoved(object sender, PointerRoutedEventArgs e) =>
        ContinueColumnResize(sender, e);

    private void BeginColumnResize(
        object sender,
        PointerRoutedEventArgs e,
        ColumnDefinition column,
        double minimumWidth,
        double maximumWidth)
    {
        if (sender is not FrameworkElement splitter)
        {
            return;
        }

        _activeSplitter = splitter;
        _activeSplitterColumn = column;
        _splitterStartPointerX = e.GetCurrentPoint(this).Position.X;
        _splitterStartWidth = column.ActualWidth;
        _splitterMinimumWidth = minimumWidth;
        _splitterMaximumWidth = Math.Max(minimumWidth, maximumWidth);
        splitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ContinueColumnResize(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, _activeSplitter) || _activeSplitterColumn is null)
        {
            return;
        }

        var pointerX = e.GetCurrentPoint(this).Position.X;
        var width = Math.Clamp(
            _splitterStartWidth + pointerX - _splitterStartPointerX,
            _splitterMinimumWidth,
            _splitterMaximumWidth);
        _activeSplitterColumn.Width = new GridLength(width);
        e.Handled = true;
    }

    private void Splitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement splitter && ReferenceEquals(splitter, _activeSplitter))
        {
            splitter.ReleasePointerCapture(e.Pointer);
            ClearActiveSplitter();
            e.Handled = true;
        }
    }

    private void Splitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        ClearActiveSplitter();

    private void ClearActiveSplitter()
    {
        _activeSplitter = null;
        _activeSplitterColumn = null;
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmCloseDirtyDocumentsAsync())
        {
            await ViewModel.OpenProjectAsync();
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.SignInAsync();

    private async void SignOut_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.SignOutAsync();

    private async void RecentProjectsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ZiapProject project)
        {
            if (await ConfirmCloseDirtyDocumentsAsync())
            {
                await ViewModel.OpenRecentProjectAsync(project);
            }
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        ViewModel.OpenCurrentProjectFolder();

    private async void ProjectExplorer_ItemInvoked(
        TreeView sender,
        TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ProjectExplorerItemViewModel item)
        {
            await ViewModel.OpenDocumentAsync(item);
        }
    }

    private void ProjectOverview_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ShowProjectOverview();

    private async void DocumentTabs_TabCloseRequested(
        TabView sender,
        TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.DataContext is DocumentTabViewModel document &&
            await ConfirmCloseDocumentAsync(document))
        {
            ViewModel.CloseDocument(document, discardChanges: document.IsDirty);
        }
    }

    private async void SaveDocument_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.SaveSelectedDocumentAsync();

    private async void SaveAll_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.SaveAllAsync();

    private void Undo_Click(object sender, RoutedEventArgs e) =>
        ViewModel.UndoSelectedDocument();

    private void Redo_Click(object sender, RoutedEventArgs e) =>
        ViewModel.RedoSelectedDocument();

    private async void SaveKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.CanSaveDocument)
        {
            await ViewModel.SaveSelectedDocumentAsync();
        }
    }

    private async void SaveAllKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.CanSaveAll)
        {
            await ViewModel.SaveAllAsync();
        }
    }

    private void UndoKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.UndoSelectedDocument();
    }

    private void RedoKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.RedoSelectedDocument();
    }

    public async Task<bool> ConfirmCloseDirtyDocumentsAsync()
    {
        var dirtyDocuments = ViewModel.DirtyDocuments;
        if (dirtyDocuments.Count == 0)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Modifiche non salvate",
            Content = dirtyDocuments.Count == 1
                ? $"{dirtyDocuments[0].DisplayName} contiene modifiche non salvate."
                : $"{dirtyDocuments.Count} documenti contengono modifiche non salvate.",
            PrimaryButtonText = dirtyDocuments.Count == 1 ? "Salva" : "Salva tutto",
            SecondaryButtonText = "Non salvare",
            CloseButtonText = "Annulla",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            return await ViewModel.SaveAllAsync() && ViewModel.DirtyDocuments.Count == 0;
        }

        return result == ContentDialogResult.Secondary;
    }

    private async Task<bool> ConfirmCloseDocumentAsync(DocumentTabViewModel document)
    {
        if (!document.IsDirty)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Modifiche non salvate",
            Content = $"Salvare le modifiche a {document.DisplayName} prima di chiudere la tab?",
            PrimaryButtonText = "Salva",
            SecondaryButtonText = "Non salvare",
            CloseButtonText = "Annulla",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var saveResult = await ViewModel.SaveDocumentAsync(document);
            return saveResult?.IsSuccess == true && !document.IsDirty;
        }

        return result == ContentDialogResult.Secondary;
    }

    private async void ResolvedReference_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement
            {
                DataContext: RpgMakerDatabaseInspectorFieldViewModel
                {
                    NavigationTarget: not null,
                } field,
            })
        {
            await ViewModel.NavigateToReferenceAsync(field.NavigationTarget);
        }
    }

    private void OpenLocalizationInConsole_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement
            {
                DataContext: RpgMakerDatabaseInspectorFieldViewModel
                {
                    LocalizationOrigin: not null,
                } field,
            })
        {
            ViewModel.OpenLocalizationInConsole(field.LocalizationOrigin);
        }
    }

    private async void RefreshRemoteLocalization_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshRemoteLocalizationAsync();

    private async void CompareRemoteLocalization_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement
            {
                DataContext: RemoteLocalizationFileViewModel file,
            })
        {
            var comparison = await ViewModel.ComparePublishedLocalizationAsync(file);
            if (comparison is null)
            {
                return;
            }

            var dialog = new RemoteLocalizationDiffDialog(
                comparison,
                allowSynchronization: false)
            {
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
    }

    private async void SynchronizeRemoteLocalization_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                DataContext: RemoteLocalizationFileViewModel file,
            })
        {
            return;
        }

        var comparison = await ViewModel.ComparePublishedLocalizationAsync(file);
        if (comparison is null)
        {
            return;
        }

        var dialog = new RemoteLocalizationDiffDialog(
            comparison,
            allowSynchronization: true)
        {
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary &&
            await ViewModel.SynchronizePublishedLocalizationAsync(file, comparison))
        {
            await ViewModel.RefreshRemoteLocalizationAsync();
        }
    }

    private void OpenRemoteLocalizationInConsole_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement
            {
                DataContext: RemoteLocalizationFileViewModel file,
            })
        {
            ViewModel.OpenRemoteLocalizationInConsole(file);
        }
    }

    private async void InitializeProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProjectInitializationDialog(
            ViewModel.CreateInitializationDefaults(),
            ViewModel.ProjectIdGenerator)
        {
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Options is not null)
        {
            await ViewModel.InitializeProjectAsync(dialog.Options);
        }
    }

    private void ErrorInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) =>
        ViewModel.ClearError();
}
