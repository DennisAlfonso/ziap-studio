using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using ZiapStudio.Core.Fusion.Bosses;
using ZiapStudio.ViewModels;

namespace ZiapStudio.Views;

public sealed partial class FusionEncounterTimelineView : UserControl
{
    private const double LeftPadding = 38;
    private const double RightPadding = 24;
    private const double TrackY = 76;
    private const double MarkerSize = 30;
    private FusionBossDocumentViewModel? _viewModel;
    private Line? _cursor;
    private Border? _cursorLabel;
    private bool _isScrubbing;

    public FusionEncounterTimelineView()
    {
        InitializeComponent();
        DataContextChanged += Timeline_DataContextChanged;
        Loaded += Timeline_Loaded;
        Unloaded += Timeline_Unloaded;
        SizeChanged += Timeline_SizeChanged;
        KeyDown += Timeline_KeyDown;
    }

    private void Timeline_Loaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as FusionBossDocumentViewModel);
        RenderTimeline();
    }

    private void Timeline_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
        _viewModel = null;
    }

    private void Timeline_DataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args)
    {
        AttachViewModel(args.NewValue as FusionBossDocumentViewModel);
        RenderTimeline();
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
        if (args.PropertyName == nameof(FusionBossDocumentViewModel.PreviewFrame))
        {
            UpdateCursor();
        }
        else if (args.PropertyName is
            nameof(FusionBossDocumentViewModel.SelectedSequence) or
            nameof(FusionBossDocumentViewModel.SelectedTimelineStep) or
            nameof(FusionBossDocumentViewModel.TimelineMaximumFrame))
        {
            RenderTimeline();
        }
    }

    private void Timeline_SizeChanged(object sender, SizeChangedEventArgs e) => RenderTimeline();

    private void RenderTimeline()
    {
        TimelineCanvas.Children.Clear();
        _cursor = null;
        _cursorLabel = null;
        var sequence = _viewModel?.SelectedSequence;
        if (sequence is null || ActualWidth <= LeftPadding + RightPadding)
        {
            TimelineCanvas.Children.Add(new TextBlock
            {
                Text = "Nessuna sequenza da visualizzare.",
                Margin = new Thickness(14),
            });
            return;
        }

        TimelineCanvas.Width = Math.Max(1, ActualWidth);
        TimelineCanvas.Height = Math.Max(142, ActualHeight);
        DrawTrack();
        DrawTicks();
        DrawWaitRanges(sequence);
        DrawAttackRanges(sequence);
        DrawMarkers(sequence);
        DrawCursor();
        UpdateCursor();
    }

    private void DrawTrack()
    {
        var line = new Line
        {
            X1 = LeftPadding,
            X2 = Math.Max(LeftPadding, ActualWidth - RightPadding),
            Y1 = TrackY,
            Y2 = TrackY,
            Stroke = ResolveBrush("DividerStrokeColorDefaultBrush", Colors.DimGray),
            StrokeThickness = 2,
        };
        TimelineCanvas.Children.Add(line);
    }

    private void DrawTicks()
    {
        var maximum = _viewModel?.TimelineMaximumFrame ?? 1;
        const int divisions = 8;
        for (var index = 0; index <= divisions; index++)
        {
            var frame = maximum * index / divisions;
            var x = FrameToX(frame);
            TimelineCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = TrackY - 6,
                Y2 = TrackY + 6,
                Stroke = ResolveBrush("DividerStrokeColorDefaultBrush", Colors.DimGray),
                StrokeThickness = 1,
            });
            var label = new TextBlock
            {
                Text = $"{frame:0}f",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9,
                Foreground = ResolveBrush("TextFillColorTertiaryBrush", Colors.Gray),
            };
            Canvas.SetLeft(label, Math.Clamp(x - 14, 2, Math.Max(2, ActualWidth - 34)));
            Canvas.SetTop(label, TrackY + 10);
            TimelineCanvas.Children.Add(label);
        }
    }

    private void DrawWaitRanges(FusionBossSequenceViewModel sequence)
    {
        foreach (var step in sequence.Steps.Where(step =>
            step.Step.Kind == FusionBossTimelineStepKind.Wait &&
            step.Step.DurationFrames is > 0))
        {
            var start = FrameToX(step.Step.EarliestStartFrame);
            var end = FrameToX(step.Step.EarliestStartFrame + step.Step.DurationFrames!.Value);
            var wait = new Button
            {
                Width = Math.Max(3, end - start),
                Height = 10,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(110, 112, 119, 133)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(5),
                Tag = step,
            };
            wait.Click += StepButton_Click;
            ToolTipService.SetToolTip(wait, BuildToolTip(step));
            Canvas.SetLeft(wait, start);
            Canvas.SetTop(wait, TrackY - 5);
            TimelineCanvas.Children.Add(wait);
        }
    }

    private void DrawAttackRanges(FusionBossSequenceViewModel sequence)
    {
        foreach (var step in sequence.Steps.Where(step => step.AttackGeometry is not null))
        {
            var attack = step.AttackGeometry!;
            var startFrame = step.Step.EarliestStartFrame;
            var repeatDelay = attack.RepeatDelayMilliseconds * 60d / 1000d;
            var endFrame = startFrame + attack.ExecutionDelayFrames +
                repeatDelay * Math.Max(0, attack.RepeatOnUseCount - 1);
            var start = FrameToX(startFrame);
            var end = FrameToX(endFrame);
            var range = new Rectangle
            {
                Width = Math.Max(3, end - start),
                Height = 5,
                RadiusX = 2.5,
                RadiusY = 2.5,
                Fill = new SolidColorBrush(Color.FromArgb(210, 255, 91, 97)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(range, start);
            Canvas.SetTop(range, TrackY - 15);
            TimelineCanvas.Children.Add(range);
        }
    }

    private void DrawMarkers(FusionBossSequenceViewModel sequence)
    {
        var slots = new Dictionary<int, int>();
        foreach (var step in sequence.Steps.Where(IsSalientStep))
        {
            var frameKey = step.Step.EarliestStartFrame;
            slots.TryGetValue(frameKey, out var slot);
            slots[frameKey] = slot + 1;
            var x = FrameToX(frameKey);
            var top = slot % 2 == 0 ? 26d : 92d;
            var stack = slot / 2;
            var stackDirection = x > ActualWidth / 2 ? -1 : 1;
            var markerCenterX = x + stackDirection * stack * (MarkerSize + 4);
            var marker = new Button
            {
                Width = MarkerSize,
                Height = MarkerSize,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(MarkerSize / 2),
                Background = MarkerBrush(step),
                BorderBrush = ReferenceEquals(_viewModel?.SelectedTimelineStep, step)
                    ? ResolveBrush("AccentFillColorDefaultBrush", Colors.DeepSkyBlue)
                    : ResolveBrush("CardStrokeColorDefaultBrush", Colors.Gray),
                BorderThickness = new Thickness(
                    ReferenceEquals(_viewModel?.SelectedTimelineStep, step) ? 2 : 1),
                Content = new FontIcon
                {
                    Glyph = MarkerGlyph(step),
                    FontSize = 13,
                },
                Tag = step,
            };
            marker.Click += StepButton_Click;
            ToolTipService.SetToolTip(marker, BuildToolTip(step));
            Canvas.SetLeft(marker, Math.Clamp(
                markerCenterX - MarkerSize / 2,
                2,
                Math.Max(2, ActualWidth - MarkerSize - 2)));
            Canvas.SetTop(marker, top);
            TimelineCanvas.Children.Add(marker);

            var connector = new Line
            {
                X1 = markerCenterX,
                X2 = x,
                Y1 = top < TrackY ? top + MarkerSize : TrackY,
                Y2 = top < TrackY ? TrackY : top,
                Stroke = MarkerBrush(step),
                StrokeThickness = 1,
                Opacity = 0.65,
                IsHitTestVisible = false,
            };
            TimelineCanvas.Children.Insert(
                Math.Max(0, TimelineCanvas.Children.Count - 1),
                connector);
        }
    }

    private void DrawCursor()
    {
        _cursor = new Line
        {
            Y1 = 14,
            Y2 = 126,
            Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 190, 72)),
            StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        TimelineCanvas.Children.Add(_cursor);
        _cursorLabel = new Border
        {
            Padding = new Thickness(5, 2, 5, 2),
            Background = new SolidColorBrush(Color.FromArgb(235, 40, 34, 22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 190, 72)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
            },
            IsHitTestVisible = false,
        };
        TimelineCanvas.Children.Add(_cursorLabel);
    }

    private void UpdateCursor()
    {
        if (_viewModel is null || _cursor is null || _cursorLabel is null)
        {
            return;
        }
        var x = FrameToX(_viewModel.PreviewFrame);
        _cursor.X1 = x;
        _cursor.X2 = x;
        if (_cursorLabel.Child is TextBlock label)
        {
            label.Text = _viewModel.PreviewFrameText;
        }
        _cursorLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_cursorLabel, Math.Clamp(
            x - _cursorLabel.DesiredSize.Width / 2,
            2,
            Math.Max(2, ActualWidth - _cursorLabel.DesiredSize.Width - 2)));
        Canvas.SetTop(_cursorLabel, 2);
    }

    private void StepButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FusionBossTimelineStepViewModel step } &&
            _viewModel is not null)
        {
            _viewModel.SelectedTimelineStep = step;
            _viewModel.PreviewFrame = step.Step.EarliestStartFrame;
        }
    }

    private void TimelineCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsFromButton(e.OriginalSource) ||
            _viewModel?.SelectedSequence is null ||
            e.GetCurrentPoint(TimelineCanvas).Properties.PointerUpdateKind !=
                PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }
        Focus(FocusState.Pointer);
        _isScrubbing = TimelineCanvas.CapturePointer(e.Pointer);
        MoveCursorTo(e.GetCurrentPoint(TimelineCanvas).Position.X, false);
        e.Handled = true;
    }

    private static bool IsFromButton(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current is not null)
        {
            if (current is Button)
            {
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void TimelineCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isScrubbing || !e.GetCurrentPoint(TimelineCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }
        MoveCursorTo(e.GetCurrentPoint(TimelineCanvas).Position.X, false);
        e.Handled = true;
    }

    private void TimelineCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isScrubbing)
        {
            return;
        }
        MoveCursorTo(e.GetCurrentPoint(TimelineCanvas).Position.X, true);
        TimelineCanvas.ReleasePointerCapture(e.Pointer);
        _isScrubbing = false;
        e.Handled = true;
    }

    private void TimelineCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e) =>
        EndScrubbing(e.Pointer);

    private void TimelineCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        _isScrubbing = false;

    private void EndScrubbing(Pointer pointer)
    {
        if (!_isScrubbing)
        {
            return;
        }
        TimelineCanvas.ReleasePointerCapture(pointer);
        _isScrubbing = false;
    }

    private void MoveCursorTo(double x, bool selectNearestStep)
    {
        if (_viewModel?.SelectedSequence is null)
        {
            return;
        }
        var frame = XToFrame(x);
        _viewModel.PreviewFrame = frame;
        if (!selectNearestStep)
        {
            return;
        }
        var nearest = _viewModel.SelectedSequence.Steps
            .OrderBy(step => Math.Abs(step.Step.EarliestStartFrame - frame))
            .FirstOrDefault();
        if (nearest is not null)
        {
            _viewModel.SelectedTimelineStep = nearest;
            _viewModel.PreviewFrame = frame;
        }
    }

    private void Timeline_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_viewModel?.SelectedSequence is null)
        {
            return;
        }
        var delta = e.Key switch
        {
            Windows.System.VirtualKey.Left => -1,
            Windows.System.VirtualKey.Right => 1,
            Windows.System.VirtualKey.PageUp => -30,
            Windows.System.VirtualKey.PageDown => 30,
            _ => 0,
        };
        if (e.Key == Windows.System.VirtualKey.Home)
        {
            _viewModel.PreviewFrame = 0;
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.End)
        {
            _viewModel.PreviewFrame = _viewModel.TimelineMaximumFrame;
            e.Handled = true;
        }
        else if (delta != 0)
        {
            _viewModel.PreviewFrame += delta;
            e.Handled = true;
        }
    }

    private StackPanel BuildToolTip(FusionBossTimelineStepViewModel step)
    {
        var panel = new StackPanel
        {
            MaxWidth = 390,
            Spacing = 3,
        };
        panel.Children.Add(new TextBlock
        {
            Text = $"{step.TimeText} · {step.Label}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = step.Detail,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = step.TechnicalId,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = ResolveBrush("TextFillColorTertiaryBrush", Colors.Gray),
        });
        if (step.HasDataFlow)
        {
            panel.Children.Add(new TextBlock
            {
                Text = step.DataFlowText,
                FontSize = 10,
                Foreground = ResolveBrush("AccentTextFillColorPrimaryBrush", Colors.DeepSkyBlue),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        if (step.AttackGeometry is { } attack)
        {
            var targetCount = Math.Max(1, step.AttackTargets.Count);
            panel.Children.Add(new TextBlock
            {
                Text = $"Telegraph {attack.TelegraphDurationFrames}f · esecuzione +{attack.ExecutionDelayFrames}f · {targetCount} target · avvii ×{attack.RepeatOnUseCount}",
                FontSize = 11,
                Foreground = ResolveBrush("AccentTextFillColorPrimaryBrush", Colors.DeepSkyBlue),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else if (step.Step.DurationFrames is { } duration)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Da F {step.Step.EarliestStartFrame} a F {step.Step.EarliestStartFrame + duration}",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
            });
        }
        return panel;
    }

    private static bool IsSalientStep(FusionBossTimelineStepViewModel step) =>
        step.Step.Kind != FusionBossTimelineStepKind.Wait;

    private static string MarkerGlyph(FusionBossTimelineStepViewModel step)
    {
        if (step.AttackGeometry is not null) return "\uE945";
        if (step.Step.Kind == FusionBossTimelineStepKind.WaitUntil) return "\uE895";
        if (step.Step.Kind is FusionBossTimelineStepKind.Sequence or
            FusionBossTimelineStepKind.RepeatSequence) return "\uE72C";
        if (step.TechnicalId.Contains("capture", StringComparison.OrdinalIgnoreCase)) return "\uE7B3";
        if (step.TechnicalId.Contains("move", StringComparison.OrdinalIgnoreCase)) return "\uE7C2";
        if (step.TechnicalId.Contains("cue", StringComparison.OrdinalIgnoreCase)) return "\uE8BD";
        return "\uE946";
    }

    private static Brush MarkerBrush(FusionBossTimelineStepViewModel step)
    {
        var color = step.AttackGeometry is not null
            ? Color.FromArgb(255, 150, 47, 54)
            : step.Step.Kind == FusionBossTimelineStepKind.WaitUntil
                ? Color.FromArgb(255, 49, 112, 150)
                : step.Step.Kind is FusionBossTimelineStepKind.Sequence or
                    FusionBossTimelineStepKind.RepeatSequence
                    ? Color.FromArgb(255, 114, 78, 156)
                    : Color.FromArgb(255, 63, 68, 80);
        return new SolidColorBrush(color);
    }

    private double FrameToX(double frame)
    {
        var width = Math.Max(1, ActualWidth - LeftPadding - RightPadding);
        var maximum = Math.Max(1, _viewModel?.TimelineMaximumFrame ?? 1);
        return LeftPadding + Math.Clamp(frame / maximum, 0, 1) * width;
    }

    private double XToFrame(double x)
    {
        var width = Math.Max(1, ActualWidth - LeftPadding - RightPadding);
        var progress = Math.Clamp((x - LeftPadding) / width, 0, 1);
        return progress * Math.Max(1, _viewModel?.TimelineMaximumFrame ?? 1);
    }

    private static Brush ResolveBrush(string resourceName, Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(resourceName, out var value) &&
            value is Brush brush)
        {
            return brush;
        }
        return new SolidColorBrush(fallback);
    }
}
