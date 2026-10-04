using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using ZiapStudio.ViewModels;

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
}
