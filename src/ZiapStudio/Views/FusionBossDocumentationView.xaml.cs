using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiapStudio.ViewModels;

namespace ZiapStudio.Views;

public sealed partial class FusionBossDocumentationView : UserControl
{
    public FusionBossDocumentationView(FusionBossDocumentationViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public FusionBossDocumentationViewModel ViewModel { get; }

    private void DocumentationNavigation_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        var showActionApi = tag.Equals("action-api", StringComparison.Ordinal);
        OverviewPage.Visibility = showActionApi ? Visibility.Collapsed : Visibility.Visible;
        ActionApiPage.Visibility = showActionApi ? Visibility.Visible : Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(() =>
        {
            OverviewNavigationItem.IsSelected = !showActionApi;
            ActionApiNavigationItem.IsSelected = showActionApi;
            sender.SelectedItem = showActionApi
                ? ActionApiNavigationItem
                : OverviewNavigationItem;
        });
    }
}
