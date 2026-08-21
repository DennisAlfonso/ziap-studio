using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using ZiapStudio.Core.Fusion.Bosses;
using ZiapStudio.ViewModels;

namespace ZiapStudio.Views;

public sealed partial class FusionExecutionGraphView : UserControl
{
    private const double NodeWidth = 226;
    private const double NodeHeight = 112;
    private const double HorizontalGap = 126;
    private const double VerticalGap = 38;
    private FusionBossDocumentViewModel? _viewModel;

    public FusionExecutionGraphView()
    {
        InitializeComponent();
        DataContextChanged += Graph_DataContextChanged;
        Loaded += Graph_Loaded;
        Unloaded += Graph_Unloaded;
    }

    private void Graph_Loaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as FusionBossDocumentViewModel);
        RenderGraph();
    }

    private void Graph_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
        _viewModel = null;
    }

    private void Graph_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        AttachViewModel(args.NewValue as FusionBossDocumentViewModel);
        RenderGraph();
    }

    private void AttachViewModel(FusionBossDocumentViewModel? viewModel)
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
        if (args.PropertyName is nameof(FusionBossDocumentViewModel.SelectedPhase) or
            nameof(FusionBossDocumentViewModel.SelectedExecutionNode) or
            nameof(FusionBossDocumentViewModel.SelectedSequence))
        {
            RenderGraph();
        }
    }

    private void RenderGraph()
    {
        GraphCanvas.Children.Clear();
        var graph = _viewModel?.ExecutionGraph;
        if (graph is null || graph.Nodes.Count == 0)
        {
            GraphCanvas.Children.Add(new TextBlock
            {
                Text = "Nessun flusso disponibile per questa fase.",
                Margin = new Thickness(18),
            });
            return;
        }

        var levels = CalculateLevels(graph);
        var positions = CalculatePositions(graph, levels);
        GraphCanvas.Width = Math.Max(
            760,
            positions.Values.Max(position => position.X) + NodeWidth + 36);
        GraphCanvas.Height = Math.Max(
            360,
            positions.Values.Max(position => position.Y) + NodeHeight + 36);

        DrawEdges(graph, positions);
        foreach (var node in graph.Nodes)
        {
            DrawNode(node, positions[node.Id]);
        }
    }

    private static IReadOnlyDictionary<string, int> CalculateLevels(
        FusionBossExecutionGraph graph)
    {
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<FusionBossExecutionNode>();
        foreach (var root in graph.Nodes.Where(node => node.Kind is
            FusionBossExecutionNodeKind.PhaseEntry or
            FusionBossExecutionNodeKind.PhaseExit))
        {
            levels[root.Id] = 0;
            queue.Enqueue(root);
        }

        while (queue.Count > 0)
        {
            var source = queue.Dequeue();
            var nextLevel = levels[source.Id] + 1;
            foreach (var edge in graph.Edges.Where(edge => edge.SourceNodeId.Equals(
                source.Id,
                StringComparison.OrdinalIgnoreCase)))
            {
                if (levels.ContainsKey(edge.TargetNodeId))
                {
                    continue;
                }
                levels[edge.TargetNodeId] = nextLevel;
                var target = graph.Nodes.FirstOrDefault(node => node.Id.Equals(
                    edge.TargetNodeId,
                    StringComparison.OrdinalIgnoreCase));
                if (target is not null)
                {
                    queue.Enqueue(target);
                }
            }
        }

        foreach (var node in graph.Nodes.Where(node => !levels.ContainsKey(node.Id)))
        {
            levels[node.Id] = 1;
        }
        return levels;
    }

    private static IReadOnlyDictionary<string, Point> CalculatePositions(
        FusionBossExecutionGraph graph,
        IReadOnlyDictionary<string, int> levels)
    {
        var result = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in graph.Nodes
            .GroupBy(node => levels[node.Id])
            .OrderBy(group => group.Key))
        {
            var row = 0;
            foreach (var node in group
                .OrderBy(NodeOrder)
                .ThenBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                result[node.Id] = new Point(
                    26 + group.Key * (NodeWidth + HorizontalGap),
                    26 + row++ * (NodeHeight + VerticalGap));
            }
        }
        return result;
    }

    private static int NodeOrder(FusionBossExecutionNode node) => node.Kind switch
    {
        FusionBossExecutionNodeKind.PhaseEntry => 0,
        FusionBossExecutionNodeKind.PhaseExit => 1,
        FusionBossExecutionNodeKind.PhaseHook => 2,
        FusionBossExecutionNodeKind.AutomaticSequence => 3,
        FusionBossExecutionNodeKind.ManualSequence => 4,
        _ => 5,
    };

    private void DrawEdges(
        FusionBossExecutionGraph graph,
        IReadOnlyDictionary<string, Point> positions)
    {
        var selectedId = _viewModel?.SelectedExecutionNode?.Id;
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
            var stroke = edge.Kind == FusionBossExecutionEdgeKind.Repeat
                ? ResolveBrush("SystemFillColorCautionBrush", Colors.Goldenrod)
                : focused
                    ? ResolveBrush("AccentFillColorDefaultBrush", Colors.DeepSkyBlue)
                    : ResolveBrush("TextFillColorSecondaryBrush", Colors.Gray);
            var opacity = focused ? 0.95 : 0.42;
            GraphCanvas.Children.Add(new Polyline
            {
                Stroke = stroke,
                StrokeThickness = focused ? 2.4 : 1.4,
                Opacity = opacity,
                Points = BuildEdgePoints(start, end),
            });
            GraphCanvas.Children.Add(new Polygon
            {
                Fill = stroke,
                Opacity = opacity,
                Points = new PointCollection
                {
                    new(end.X, end.Y),
                    new(end.X - 8, end.Y - 5),
                    new(end.X - 8, end.Y + 5),
                },
            });

            var label = new Button
            {
                Content = edge.Label,
                Tag = edge,
                Padding = new Thickness(7, 2, 7, 2),
                FontSize = 9,
                MinHeight = 24,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            label.Click += EdgeButton_Click;
            ToolTipService.SetToolTip(label, $"{edge.Label}\n{edge.Detail}\nClick: seleziona lo step chiamante.");
            Canvas.SetLeft(label, start.X + Math.Max(10, (end.X - start.X) / 2 - 34));
            Canvas.SetTop(label, (start.Y + end.Y) / 2 - 13);
            GraphCanvas.Children.Add(label);
        }
    }

    private static PointCollection BuildEdgePoints(Point start, Point end)
    {
        if (end.X > start.X)
        {
            var middleX = start.X + (end.X - start.X) / 2;
            return [start, new Point(middleX, start.Y), new Point(middleX, end.Y), end];
        }
        var detourY = Math.Max(start.Y, end.Y) + NodeHeight / 2 + 24;
        return
        [
            start,
            new Point(start.X + 22, start.Y),
            new Point(start.X + 22, detourY),
            new Point(end.X - 22, detourY),
            new Point(end.X - 22, end.Y),
            end,
        ];
    }

    private void DrawNode(FusionBossExecutionNode node, Point position)
    {
        var isSelected = node.Id.Equals(
            _viewModel?.SelectedExecutionNode?.Id,
            StringComparison.OrdinalIgnoreCase);
        var borderBrush = node.IsWarning
            ? ResolveBrush("SystemFillColorCautionBrush", Colors.DarkOrange)
            : isSelected
                ? ResolveBrush("AccentFillColorDefaultBrush", Colors.DeepSkyBlue)
                : ResolveBrush("CardStrokeColorDefaultBrush", Colors.Gray);
        var button = new Button
        {
            Width = NodeWidth,
            Height = NodeHeight,
            Padding = new Thickness(12, 9, 12, 9),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            BorderBrush = borderBrush,
            Tag = node,
        };
        var content = new Grid { RowSpacing = 2 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

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

        var technical = new TextBlock
        {
            Text = node.TechnicalId,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            Foreground = ResolveBrush("TextFillColorTertiaryBrush", Colors.Gray),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(technical, 1);
        content.Children.Add(technical);

        var metrics = new TextBlock
        {
            Text = node.CanOpenTimeline
                ? $"{node.StepCountText} · {node.DurationSummary}"
                : node.Kind == FusionBossExecutionNodeKind.MissingReference
                    ? "definizione non trovata"
                    : "evento del ciclo di vita",
            FontSize = 9,
            Foreground = ResolveBrush("TextFillColorSecondaryBrush", Colors.Gray),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(metrics, 2);
        content.Children.Add(metrics);

        var reason = new TextBlock
        {
            Text = node.StartReason,
            FontSize = 10,
            Foreground = ResolveBrush("TextFillColorSecondaryBrush", Colors.Gray),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(reason, 3);
        content.Children.Add(reason);

        button.Content = content;
        button.Click += NodeButton_Click;
        ToolTipService.SetToolTip(
            button,
            $"{node.StartReason}\n\n{node.CallerSummary}\n\n{node.OutcomeSummary}");
        Canvas.SetLeft(button, position.X);
        Canvas.SetTop(button, position.Y);
        GraphCanvas.Children.Add(button);
    }

    private void NodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FusionBossExecutionNode node } && _viewModel is not null)
        {
            _viewModel.SelectedExecutionNode = node;
        }
    }

    private void EdgeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FusionBossExecutionEdge edge } && _viewModel is not null)
        {
            _viewModel.SelectExecutionEdge(edge);
        }
    }

    private static Brush ResolveBrush(string resourceName, Windows.UI.Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(resourceName, out var value) &&
            value is Brush brush)
        {
            return brush;
        }
        return new SolidColorBrush(fallback);
    }
}
