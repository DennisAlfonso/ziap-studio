using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using ZiapStudio.Core.Fusion.Puzzles;
using ZiapStudio.ViewModels;

namespace ZiapStudio.Views;

public sealed partial class FusionPuzzleGraphView : UserControl
{
    private const double NodeWidth = 214;
    private const double NodeHeight = 106;
    private const double HorizontalGap = 104;
    private const double VerticalGap = 34;
    private FusionPuzzleDocumentViewModel? _viewModel;

    public FusionPuzzleGraphView()
    {
        InitializeComponent();
        DataContextChanged += Graph_DataContextChanged;
        Loaded += Graph_Loaded;
        Unloaded += Graph_Unloaded;
    }

    private void Graph_Loaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as FusionPuzzleDocumentViewModel);
        RenderGraph();
    }

    private void Graph_Unloaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(null);
    }

    private void Graph_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        AttachViewModel(args.NewValue as FusionPuzzleDocumentViewModel);
        RenderGraph();
    }

    private void AttachViewModel(FusionPuzzleDocumentViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(FusionPuzzleDocumentViewModel.SelectedPuzzle) or
            nameof(FusionPuzzleDocumentViewModel.SelectedGraphNode))
        {
            RenderGraph();
        }
    }

    private void RenderGraph()
    {
        GraphCanvas.Children.Clear();
        var graph = _viewModel?.Graph;
        if (graph is null || graph.Nodes.Count == 0)
        {
            GraphCanvas.Children.Add(new TextBlock
            {
                Text = "Nessun flusso puzzle disponibile.",
                Margin = new Thickness(20),
            });
            return;
        }

        var positions = CalculatePositions(graph);
        GraphCanvas.Width = Math.Max(840, positions.Values.Max(point => point.X) + NodeWidth + 40);
        GraphCanvas.Height = Math.Max(380, positions.Values.Max(point => point.Y) + NodeHeight + 40);
        DrawEdges(graph, positions);
        foreach (var node in graph.Nodes)
        {
            DrawNode(node, positions[node.Id]);
        }
    }

    private static IReadOnlyDictionary<string, Point> CalculatePositions(FusionPuzzleGraph graph)
    {
        var positions = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        foreach (var level in graph.Nodes.GroupBy(node => node.Level).OrderBy(group => group.Key))
        {
            var row = 0;
            foreach (var node in level.OrderBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                positions[node.Id] = new Point(
                    28 + level.Key * (NodeWidth + HorizontalGap),
                    28 + row++ * (NodeHeight + VerticalGap));
            }
        }
        return positions;
    }

    private void DrawEdges(
        FusionPuzzleGraph graph,
        IReadOnlyDictionary<string, Point> positions)
    {
        var selectedId = _viewModel?.SelectedGraphNode?.Id;
        foreach (var edge in graph.Edges)
        {
            if (!positions.TryGetValue(edge.SourceNodeId, out var source) ||
                !positions.TryGetValue(edge.TargetNodeId, out var target))
            {
                continue;
            }
            var start = new Point(source.X + NodeWidth, source.Y + NodeHeight / 2);
            var end = new Point(target.X, target.Y + NodeHeight / 2);
            var focused = edge.SourceNodeId.Equals(selectedId, StringComparison.OrdinalIgnoreCase) ||
                edge.TargetNodeId.Equals(selectedId, StringComparison.OrdinalIgnoreCase);
            var stroke = focused
                ? ResolveBrush("AccentFillColorDefaultBrush", Colors.DeepSkyBlue)
                : ResolveBrush("TextFillColorSecondaryBrush", Colors.SlateGray);
            var middleX = start.X + (end.X - start.X) / 2;
            GraphCanvas.Children.Add(new Polyline
            {
                Stroke = stroke,
                StrokeThickness = focused ? 2.4 : 1.4,
                Opacity = focused ? 0.95 : 0.42,
                Points = new PointCollection
                {
                    start,
                    new(middleX, start.Y),
                    new(middleX, end.Y),
                    end,
                },
            });
            GraphCanvas.Children.Add(new Polygon
            {
                Fill = stroke,
                Opacity = focused ? 0.95 : 0.42,
                Points = new PointCollection
                {
                    end,
                    new(end.X - 8, end.Y - 5),
                    new(end.X - 8, end.Y + 5),
                },
            });
            var label = new TextBlock
            {
                Text = edge.Label,
                FontSize = 9,
                Padding = new Thickness(5, 2, 5, 2),
            };
            ToolTipService.SetToolTip(label, edge.Detail);
            Canvas.SetLeft(label, middleX - 28);
            Canvas.SetTop(label, (start.Y + end.Y) / 2 - 12);
            GraphCanvas.Children.Add(label);
        }
    }

    private void DrawNode(FusionPuzzleGraphNode node, Point position)
    {
        var selected = node.Id.Equals(_viewModel?.SelectedGraphNode?.Id, StringComparison.OrdinalIgnoreCase);
        var button = new Button
        {
            Width = NodeWidth,
            Height = NodeHeight,
            Padding = new Thickness(12, 9, 12, 9),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = node.IsWarning
                ? ResolveBrush("SystemFillColorCautionBrush", Colors.DarkOrange)
                : selected
                    ? ResolveBrush("AccentFillColorDefaultBrush", Colors.DeepSkyBlue)
                    : ResolveBrush("CardStrokeColorDefaultBrush", Colors.Gray),
            Tag = node,
        };
        var content = new StackPanel { Spacing = 3 };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = node.DisplayName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var badge = new TextBlock
        {
            Text = node.BadgeText,
            FontSize = 9,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = node.IsWarning
                ? ResolveBrush("SystemFillColorCautionBrush", Colors.DarkOrange)
                : ResolveBrush("AccentTextFillColorPrimaryBrush", Colors.DeepSkyBlue),
        };
        Grid.SetColumn(badge, 1);
        header.Children.Add(badge);
        content.Children.Add(header);
        content.Children.Add(new TextBlock
        {
            Text = node.TechnicalId,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            Foreground = ResolveBrush("TextFillColorTertiaryBrush", Colors.Gray),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        content.Children.Add(new TextBlock
        {
            Text = node.Description,
            FontSize = 10,
            Foreground = ResolveBrush("TextFillColorSecondaryBrush", Colors.Gray),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        button.Content = content;
        button.Click += Node_Click;
        ToolTipService.SetToolTip(button, $"{node.Description}\n\n{node.Execution}\n\n{node.Outcome}");
        Canvas.SetLeft(button, position.X);
        Canvas.SetTop(button, position.Y);
        GraphCanvas.Children.Add(button);
    }

    private void Node_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FusionPuzzleGraphNode node } && _viewModel is not null)
        {
            _viewModel.SelectedGraphNode = node;
        }
    }

    private static Brush ResolveBrush(string resourceName, Windows.UI.Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(resourceName, out var value) && value is Brush brush)
        {
            return brush;
        }
        return new SolidColorBrush(fallback);
    }
}
