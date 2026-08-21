using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using ZiapStudio.ViewModels;

namespace ZiapStudio.Views;

public sealed partial class FusionEncounterGraphView : UserControl
{
    private const double NodeWidth = 174;
    private const double NodeHeight = 88;
    private const double HorizontalGap = 92;
    private const double VerticalGap = 36;
    private FusionBossDocumentViewModel? _viewModel;

    public FusionEncounterGraphView()
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
        if (args.PropertyName is nameof(FusionBossDocumentViewModel.SelectedEncounter) or
            nameof(FusionBossDocumentViewModel.SelectedPhase))
        {
            RenderGraph();
        }
    }

    private void RenderGraph()
    {
        GraphCanvas.Children.Clear();
        var encounter = _viewModel?.SelectedEncounter;
        if (encounter is null || encounter.Phases.Count == 0)
        {
            GraphCanvas.Children.Add(new TextBlock
            {
                Text = "Nessuna fase disponibile.",
                Margin = new Thickness(18),
            });
            return;
        }

        var levels = CalculateLevels(encounter);
        var positions = CalculatePositions(encounter, levels);
        GraphCanvas.Width = Math.Max(
            600,
            positions.Values.Max(position => position.X) + NodeWidth + 30);
        GraphCanvas.Height = Math.Max(
            220,
            positions.Values.Max(position => position.Y) + NodeHeight + 30);

        DrawTransitions(encounter, positions);
        foreach (var phase in encounter.Phases)
        {
            DrawPhaseNode(phase, positions[phase.Id]);
        }
    }

    private static IReadOnlyDictionary<string, int> CalculateLevels(
        FusionBossEncounterViewModel encounter)
    {
        var phases = encounter.Phases.ToDictionary(phase => phase.Id, StringComparer.OrdinalIgnoreCase);
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var initial = encounter.Phases.FirstOrDefault(phase => phase.IsInitial) ?? encounter.Phases[0];
        var queue = new Queue<FusionBossPhaseViewModel>();
        levels[initial.Id] = 0;
        queue.Enqueue(initial);
        while (queue.Count > 0)
        {
            var phase = queue.Dequeue();
            var nextLevel = levels[phase.Id] + 1;
            foreach (var transition in phase.Phase.Transitions)
            {
                if (!phases.TryGetValue(transition.TargetPhaseId, out var target) ||
                    levels.ContainsKey(target.Id))
                {
                    continue;
                }
                levels[target.Id] = nextLevel;
                queue.Enqueue(target);
            }
        }

        var fallbackLevel = levels.Count == 0 ? 0 : levels.Values.Max() + 1;
        foreach (var phase in encounter.Phases.Where(phase => !levels.ContainsKey(phase.Id)))
        {
            levels[phase.Id] = fallbackLevel++;
        }
        return levels;
    }

    private static IReadOnlyDictionary<string, Point> CalculatePositions(
        FusionBossEncounterViewModel encounter,
        IReadOnlyDictionary<string, int> levels)
    {
        var result = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in encounter.Phases
            .GroupBy(phase => levels[phase.Id])
            .OrderBy(group => group.Key))
        {
            var row = 0;
            foreach (var phase in group)
            {
                result[phase.Id] = new Point(
                    22 + group.Key * (NodeWidth + HorizontalGap),
                    22 + row++ * (NodeHeight + VerticalGap));
            }
        }
        return result;
    }

    private void DrawTransitions(
        FusionBossEncounterViewModel encounter,
        IReadOnlyDictionary<string, Point> positions)
    {
        var selectedPhaseId = _viewModel?.SelectedPhase?.Id;
        foreach (var phase in encounter.Phases)
        {
            var source = positions[phase.Id];
            foreach (var transition in phase.Phase.Transitions)
            {
                if (!positions.TryGetValue(transition.TargetPhaseId, out var target))
                {
                    continue;
                }
                var start = new Point(source.X + NodeWidth, source.Y + NodeHeight / 2);
                var end = new Point(target.X, target.Y + NodeHeight / 2);
                var isFocused = phase.Id.Equals(
                        selectedPhaseId,
                        StringComparison.OrdinalIgnoreCase) ||
                    transition.TargetPhaseId.Equals(
                        selectedPhaseId,
                        StringComparison.OrdinalIgnoreCase);
                var stroke = isFocused
                    ? ResolveBrush("AccentFillColorDefaultBrush", Colors.DeepSkyBlue)
                    : ResolveBrush("TextFillColorSecondaryBrush", Colors.Gray);
                var line = new Polyline
                {
                    Stroke = stroke,
                    StrokeThickness = isFocused ? 2.25 : 1.25,
                    Opacity = isFocused ? 0.95 : 0.28,
                    Points = BuildTransitionPoints(start, end),
                };
                ToolTipService.SetToolTip(
                    line,
                    $"{phase.Id} → {transition.TargetPhaseId}\n{transition.ConditionSummary}");
                GraphCanvas.Children.Add(line);

                var arrow = new Polygon
                {
                    Fill = stroke,
                    Opacity = isFocused ? 0.95 : 0.28,
                    Points = new PointCollection
                    {
                        new(end.X, end.Y),
                        new(end.X - 8, end.Y - 5),
                        new(end.X - 8, end.Y + 5),
                    },
                };
                ToolTipService.SetToolTip(
                    arrow,
                    $"{phase.Id} → {transition.TargetPhaseId}\n{transition.ConditionSummary}");
                GraphCanvas.Children.Add(arrow);
            }
        }
    }

    private static PointCollection BuildTransitionPoints(Point start, Point end)
    {
        var middleX = start.X + (end.X - start.X) / 2;
        if (end.X > start.X)
        {
            return [start, new Point(middleX, start.Y), new Point(middleX, end.Y), end];
        }
        var detourY = Math.Max(start.Y, end.Y) + NodeHeight / 2 + 24;
        return
        [
            start,
            new Point(start.X + 20, start.Y),
            new Point(start.X + 20, detourY),
            new Point(end.X - 20, detourY),
            new Point(end.X - 20, end.Y),
            end,
        ];
    }

    private void DrawPhaseNode(FusionBossPhaseViewModel phase, Point position)
    {
        var isSelected = ReferenceEquals(_viewModel?.SelectedPhase, phase);
        var button = new Button
        {
            Width = NodeWidth,
            Height = NodeHeight,
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            BorderBrush = isSelected
                ? ResolveBrush("AccentFillColorDefaultBrush", Colors.DeepSkyBlue)
                : ResolveBrush("CardStrokeColorDefaultBrush", Colors.Gray),
            Tag = phase,
        };
        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(new TextBlock
        {
            Text = phase.DisplayName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        content.Children.Add(new TextBlock
        {
            Text = phase.TechnicalIdText,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            Foreground = ResolveBrush("TextFillColorTertiaryBrush", Colors.Gray),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        content.Children.Add(new TextBlock
        {
            Text = phase.IsInitial
                ? $"INIZIALE · {phase.SequenceCountText}"
                : phase.SequenceCountText,
            FontSize = 10,
            Foreground = ResolveBrush("TextFillColorSecondaryBrush", Colors.Gray),
        });
        content.Children.Add(new TextBlock
        {
            Text = phase.TransitionCountText,
            FontSize = 10,
            Foreground = ResolveBrush("TextFillColorTertiaryBrush", Colors.Gray),
        });
        button.Content = content;
        button.Click += PhaseButton_Click;
        ToolTipService.SetToolTip(button, phase.ContextText);
        Canvas.SetLeft(button, position.X);
        Canvas.SetTop(button, position.Y);
        GraphCanvas.Children.Add(button);
    }

    private void PhaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FusionBossPhaseViewModel phase } && _viewModel is not null)
        {
            _viewModel.SelectedPhase = phase;
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
