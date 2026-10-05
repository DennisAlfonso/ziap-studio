using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using ZiapStudio.ViewModels;
using ZiapStudio.Core.Fusion.Story;

namespace ZiapStudio.Views;

public sealed partial class FusionStoryDocumentView : UserControl
{
    public FusionStoryDocumentView()
    {
        InitializeComponent();
    }

    private async void EditMasterText_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel)
        {
            await viewModel.BeginEditingAsync();
        }
    }

    private async void SaveToStaging_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel)
        {
            await viewModel.SaveToStagingAsync();
        }
    }

    private void UndoLocalization_Click(object sender, RoutedEventArgs e) =>
        (DataContext as FusionStoryDocumentViewModel)?.UndoLocalization();

    private void RedoLocalization_Click(object sender, RoutedEventArgs e) =>
        (DataContext as FusionStoryDocumentViewModel)?.RedoLocalization();

    private void DiscardLocalization_Click(object sender, RoutedEventArgs e) =>
        (DataContext as FusionStoryDocumentViewModel)?.DiscardLocalization();

    private async void RetryMirror_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel)
        {
            await viewModel.RetryMirrorAsync();
        }
    }

    private void AddDialogueAfter_Click(object sender, RoutedEventArgs e) =>
        (DataContext as FusionStoryDocumentViewModel)?.Composer.Start(StoryCompositionOperationType.AddDialogue);

    private void AddNarrationAfter_Click(object sender, RoutedEventArgs e) =>
        (DataContext as FusionStoryDocumentViewModel)?.Composer.Start(StoryCompositionOperationType.AddNarration);

    private async void PreviewComposition_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel)
        {
            await viewModel.Composer.PreviewAsync();
        }
    }

    private async void CommitComposition_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel)
        {
            await viewModel.Composer.CommitAsync();
        }
    }

    private async void UnlinkStoryBlock_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel)
        {
            await viewModel.Composer.UnlinkAsync();
        }
    }

    private async void CompleteRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel)
        {
            await viewModel.Composer.CompleteRecoveryAsync();
        }
    }

    private async void DismissRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel)
        {
            await viewModel.Composer.DismissRecoveryAsync();
        }
    }

    private void MapTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel &&
            args.InvokedItem is StoryMapTreeItemViewModel map)
        {
            viewModel.Navigator.SelectMap(map);
        }
    }

    private void Event_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel &&
            sender is FrameworkElement {Tag: StoryEventNavigatorItemViewModel @event})
        {
            viewModel.Navigator.SelectEvent(@event);
        }
    }

    private void Page_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel &&
            sender is FrameworkElement {Tag: StoryPageNavigatorItemViewModel page})
        {
            viewModel.Navigator.SelectPage(page);
            ScrollToSelectedBlock(viewModel);
        }
    }

    private void CommonEvent_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel &&
            e.ClickedItem is StoryCommonEventNavigatorItemViewModel commonEvent)
        {
            viewModel.Navigator.SelectCommonEvent(commonEvent);
            ScrollToSelectedBlock(viewModel);
        }
    }

    private void SearchResult_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel && e.ClickedItem is StorySearchResult result)
        {
            viewModel.Navigator.Navigate(result);
            ScrollToSelectedBlock(viewModel);
        }
    }

    private void ShowMaps_Click(object sender, RoutedEventArgs e) =>
        (DataContext as FusionStoryDocumentViewModel)?.Navigator.ShowMaps();

    private void ShowCommonEvents_Click(object sender, RoutedEventArgs e) =>
        (DataContext as FusionStoryDocumentViewModel)?.Navigator.ShowCommonEvents();

    private void ClearFilters_Click(object sender, RoutedEventArgs e) =>
        (DataContext as FusionStoryDocumentViewModel)?.Navigator.Filters.Clear();

    private void FilterSourceAll_Click(object sender, RoutedEventArgs e) => SetFilterSource(StorySearchSourceFilter.All);
    private void FilterSourceMaps_Click(object sender, RoutedEventArgs e) => SetFilterSource(StorySearchSourceFilter.Maps);
    private void FilterSourceCommon_Click(object sender, RoutedEventArgs e) => SetFilterSource(StorySearchSourceFilter.CommonEvents);

    private void SetFilterSource(StorySearchSourceFilter source)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel)
        {
            viewModel.Navigator.Filters.Source = source;
        }
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        StorySearchBox.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    private void ClearSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (DataContext is FusionStoryDocumentViewModel viewModel && viewModel.Navigator.IsSearchMode)
        {
            viewModel.Navigator.ClearSearch();
            args.Handled = true;
        }
    }

    private void ScrollToSelectedBlock(FusionStoryDocumentViewModel viewModel)
    {
        if (viewModel.SelectedBlock is not null)
        {
            TimelineList.ScrollIntoView(viewModel.SelectedBlock);
        }
    }
}
