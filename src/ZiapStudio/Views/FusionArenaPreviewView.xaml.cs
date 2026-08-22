using System.ComponentModel;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using ZiapStudio.ViewModels;

namespace ZiapStudio.Views;

public sealed partial class FusionArenaPreviewView : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private FusionBossDocumentViewModel? _viewModel;
    private bool _rendererReady;
    private bool _initialized;
    private bool _rendererReleased;
    private bool _updatingPreviewFrameFromRenderer;
    private bool _updatingTimelineStepFromRenderer;
    private string? _mappedProjectPath;

    public FusionArenaPreviewView()
    {
        InitializeComponent();
        DataContextChanged += Preview_DataContextChanged;
        Loaded += Preview_Loaded;
        Unloaded += Preview_Unloaded;
    }

    private async void Preview_Loaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as FusionBossDocumentViewModel);
        await EnsureRendererAsync();
        SendCurrentScene();
    }

    private void Preview_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
        _viewModel = null;
    }

    internal void ReleaseRenderer()
    {
        if (_rendererReleased)
        {
            return;
        }
        _rendererReleased = true;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel = null;
        }
        if (PreviewWebView.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= Core_WebMessageReceived;
            core.ProcessFailed -= Core_ProcessFailed;
            core.NavigationCompleted -= Core_NavigationCompleted;
        }
        PreviewWebView.Close();
        _rendererReady = false;
    }

    private async void Preview_DataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args)
    {
        AttachViewModel(args.NewValue as FusionBossDocumentViewModel);
        if (IsLoaded)
        {
            await EnsureRendererAsync();
            SendCurrentScene();
        }
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
        if (args.PropertyName == nameof(FusionBossDocumentViewModel.SelectedMap))
        {
            SendCurrentScene();
        }
        else if (args.PropertyName is
            nameof(FusionBossDocumentViewModel.SelectedAttackGeometry) or
            nameof(FusionBossDocumentViewModel.SelectedAttackTarget) or
            nameof(FusionBossDocumentViewModel.RuntimeTraceAnalysis))
        {
            if (!_updatingTimelineStepFromRenderer)
            {
                SendAttackGeometry();
            }
        }
        else if (args.PropertyName == nameof(FusionBossDocumentViewModel.PreviewFrame) &&
            !_updatingPreviewFrameFromRenderer)
        {
            SendSchedulerFrame(true);
        }
    }

    private async Task EnsureRendererAsync()
    {
        if (_initialized || _viewModel is null)
        {
            return;
        }
        _initialized = true;
        try
        {
            HostStatus.Text = "Avvio del Tilemap MZ…";
            await PreviewWebView.EnsureCoreWebView2Async();
            var core = PreviewWebView.CoreWebView2;
            ConfigureSecurity(core);
            ConfigureResourceMappings(core, _viewModel.ProjectPath);
            core.WebMessageReceived += Core_WebMessageReceived;
            core.ProcessFailed += Core_ProcessFailed;
            core.NavigationCompleted += Core_NavigationCompleted;
            PreviewWebView.Source = new Uri("https://preview.ziap/index.html");
        }
        catch (Exception exception)
        {
            ShowHostError(exception.Message);
        }
    }

    private static void ConfigureSecurity(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.NavigationStarting += (_, args) =>
        {
            if (!IsAllowedNavigationUri(args.Uri))
            {
                args.Cancel = true;
            }
        };
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (IsAllowedResourceUri(args.Request.Uri))
            {
                return;
            }
            args.Response = core.Environment.CreateWebResourceResponse(
                null,
                403,
                "Forbidden",
                string.Empty);
        };
    }

    private void ConfigureResourceMappings(CoreWebView2 core, string projectPath)
    {
        var previewRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "ArenaPreview");
        if (!Directory.Exists(previewRoot))
        {
            throw new IOException($"Asset Arena Preview mancanti: {previewRoot}");
        }
        if (!Directory.Exists(projectPath))
        {
            throw new IOException($"Cartella progetto non disponibile: {projectPath}");
        }
        core.SetVirtualHostNameToFolderMapping(
            "preview.ziap",
            previewRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.SetVirtualHostNameToFolderMapping(
            "project.ziap",
            projectPath,
            CoreWebView2HostResourceAccessKind.Allow);
        _mappedProjectPath = projectPath;
    }

    private static bool IsAllowedNavigationUri(string rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return false;
        }
        if (uri.Scheme == "about")
        {
            return true;
        }
        return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
            uri.Host.Equals("preview.ziap", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedResourceUri(string rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return false;
        }
        if (uri.Scheme is "about" or "data" or "blob")
        {
            return true;
        }
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (uri.Host.Equals("preview.ziap", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!uri.Host.Equals("project.ziap", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var path = Uri.UnescapeDataString(uri.AbsolutePath).Replace('\\', '/');
        return path.Equals("/js/libs/pixi.js", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/js/rmmz_core.js", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/img/tilesets/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/img/parallaxes/", StringComparison.OrdinalIgnoreCase);
    }

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            ShowHostError($"Navigazione renderer fallita: {args.WebErrorStatus}");
        }
    }

    private void Core_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args) =>
        ShowHostError($"Il processo renderer è terminato: {args.ProcessFailedKind}");

    private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            var root = message.RootElement;
            var type = root.TryGetProperty("type", out var typeValue)
                ? typeValue.GetString()
                : null;
            switch (type)
            {
                case "ready":
                    _rendererReady = true;
                    LoadingIndicator.IsActive = false;
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                    HostStatus.Text = "Tilemap MZ isolato";
                    SendCurrentScene();
                    break;
                case "sceneLoaded":
                    LoadingIndicator.IsActive = false;
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                    HostStatus.Text = _viewModel?.SelectedMap?.DisplayName ?? "Arena caricata";
                    SendAttackGeometry();
                    break;
                case "markerSelected":
                    if (root.TryGetProperty("marker", out var marker))
                    {
                        var id = marker.TryGetProperty("id", out var markerId)
                            ? markerId.GetString()
                            : null;
                        HostStatus.Text = string.IsNullOrWhiteSpace(id)
                            ? "Marker selezionato"
                            : $"Marker: {id}";
                    }
                    break;
                case "schedulerFrameChanged":
                    if (_viewModel is not null &&
                        root.TryGetProperty("frame", out var frameValue) &&
                        frameValue.TryGetDouble(out var frame))
                    {
                        _updatingPreviewFrameFromRenderer = true;
                        try
                        {
                            _viewModel.PreviewFrame = frame;
                        }
                        finally
                        {
                            _updatingPreviewFrameFromRenderer = false;
                        }
                    }
                    break;
                case "timelineStepSelected":
                    SelectTimelineStepFromRenderer(root);
                    break;
                case "error":
                    var error = root.TryGetProperty("message", out var errorValue)
                        ? errorValue.GetString()
                        : "Errore renderer sconosciuto.";
                    ShowHostError(error ?? "Errore renderer sconosciuto.");
                    break;
            }
        }
        catch (JsonException exception)
        {
            ShowHostError($"Messaggio renderer non valido: {exception.Message}");
        }
    }

    private void SelectTimelineStepFromRenderer(JsonElement root)
    {
        if (_viewModel?.SelectedSequence is null ||
            !root.TryGetProperty("stepIndex", out var stepIndexValue) ||
            !stepIndexValue.TryGetInt32(out var stepIndex))
        {
            return;
        }
        var step = _viewModel.SelectedSequence.Steps
            .FirstOrDefault(candidate => candidate.Step.Index == stepIndex);
        if (step is null)
        {
            return;
        }
        var frame = root.TryGetProperty("frame", out var frameValue) &&
            frameValue.TryGetDouble(out var selectedFrame)
                ? selectedFrame
                : step.Step.EarliestStartFrame;
        _updatingPreviewFrameFromRenderer = true;
        _updatingTimelineStepFromRenderer = true;
        try
        {
            _viewModel.SelectedTimelineStep = step;
            _viewModel.PreviewFrame = frame;
        }
        finally
        {
            _updatingTimelineStepFromRenderer = false;
            _updatingPreviewFrameFromRenderer = false;
        }
        SendAttackGeometry();
        HostStatus.Text = $"{step.TimeText} · {step.Label}";
    }

    private void SendCurrentScene()
    {
        var core = PreviewWebView.CoreWebView2;
        var viewModel = _viewModel;
        if (!_rendererReady || core is null || viewModel is null)
        {
            return;
        }
        if (!string.Equals(
            _mappedProjectPath,
            viewModel.ProjectPath,
            StringComparison.OrdinalIgnoreCase))
        {
            ShowHostError("Il progetto della preview è cambiato; riaprire il documento.");
            return;
        }
        if (viewModel.SelectedMap is null)
        {
            core.PostWebMessageAsJson("{\"type\":\"clearScene\"}");
            LoadingIndicator.IsActive = false;
            LoadingIndicator.Visibility = Visibility.Collapsed;
            HostStatus.Text = "Nessuna mappa selezionata";
            return;
        }
        var payload = JsonSerializer.Serialize(
            new
            {
                type = "loadScene",
                scene = viewModel.SelectedMap.Scene,
            },
            JsonOptions);
        LoadingIndicator.Visibility = Visibility.Visible;
        LoadingIndicator.IsActive = true;
        core.PostWebMessageAsJson(payload);
        HostStatus.Text = $"Caricamento {viewModel.SelectedMap.DisplayName}…";
        SendAttackGeometry();
    }

    private void SendAttackGeometry()
    {
        var core = PreviewWebView.CoreWebView2;
        var viewModel = _viewModel;
        if (!_rendererReady || core is null || viewModel?.SelectedMap is null)
        {
            return;
        }
        var payload = JsonSerializer.Serialize(
            new
            {
                type = "setAttackGeometry",
                attack = viewModel.SelectedAttackGeometry is null
                    ? null
                    : new
                    {
                        geometry = viewModel.SelectedAttackGeometry,
                        target = viewModel.SelectedAttackTarget,
                        targets = viewModel.SelectedTimelineStep?.AttackTargets ?? [],
                        stepIndex = viewModel.SelectedTimelineStep?.Step.Index ?? 0,
                    },
                sequence = viewModel.SelectedSequence is null
                    ? null
                    : new
                    {
                        id = viewModel.SelectedSequence.Id,
                        minimumDurationFrames = viewModel.SelectedSequence.Sequence.MinimumDurationFrames,
                        hasDynamicTiming = viewModel.SelectedSequence.Sequence.HasDynamicTiming,
                        firstInexactFrame = viewModel.TimelineFirstInexactFrame,
                        previewFrame = viewModel.PreviewFrame,
                        selectedStepIndex = viewModel.SelectedTimelineStep?.Step.Index ?? -1,
                        steps = viewModel.SelectedSequence.Steps
                            .Select(step => new
                            {
                                stepIndex = step.Step.Index,
                                frame = step.Step.EarliestStartFrame,
                                isStartExact = step.Step.IsStartExact,
                                kind = step.Step.Kind.ToString(),
                                label = step.Label,
                                detail = step.Detail,
                                durationFrames = step.Step.DurationFrames,
                                timeoutFrames = step.Step.TimeoutFrames,
                                attackLifecycleId = step.Step.AttackLifecycleId,
                                attackLifecycleStage = step.Step.AttackLifecycleStage,
                                linkedAttackStepIndex = step.Step.LinkedAttackStepIndex,
                                attack = step.AttackGeometry is null
                                    ? null
                                    : new
                                    {
                                        skillId = step.AttackGeometry.SkillId,
                                        skillName = step.AttackGeometry.SkillName,
                                        telegraphDurationFrames = step.AttackGeometry.TelegraphDurationFrames,
                                        executionDelayFrames = step.AttackGeometry.ExecutionDelayFrames,
                                        repeatOnUseCount = step.AttackGeometry.RepeatOnUseCount,
                                        hitRepeatCount = step.AttackGeometry.HitRepeatCount,
                                        targetCount = Math.Max(1, step.AttackTargets.Count),
                                        lifecycleImpactFrame = step.Step.LinkedAttackStepIndex is { } impactIndex
                                            ? viewModel.SelectedSequence.Steps
                                                .FirstOrDefault(candidate => candidate.Step.Index == impactIndex)?
                                                .Step.EarliestStartFrame
                                            : null,
                                        lifecycleImpactIsExact = step.Step.LinkedAttackStepIndex is { } exactImpactIndex &&
                                            viewModel.SelectedSequence.Steps
                                                .FirstOrDefault(candidate => candidate.Step.Index == exactImpactIndex)?
                                                .Step.IsStartExact == true,
                                    },
                            })
                            .ToArray(),
                        attacks = viewModel.SelectedSequence.Steps
                            .Where(step => step.AttackGeometry is not null)
                            .Select(step => new
                            {
                                stepIndex = step.Step.Index,
                                startFrame = step.Step.EarliestStartFrame,
                                isStartExact = step.Step.IsStartExact,
                                attackLifecycleId = step.Step.AttackLifecycleId,
                                attackLifecycleStage = step.Step.AttackLifecycleStage,
                                lifecycleImpactFrame = step.Step.LinkedAttackStepIndex is { } impactIndex
                                    ? viewModel.SelectedSequence.Steps
                                        .FirstOrDefault(candidate => candidate.Step.Index == impactIndex)?
                                        .Step.EarliestStartFrame
                                    : null,
                                lifecycleImpactIsExact = step.Step.LinkedAttackStepIndex is { } exactImpactIndex &&
                                    viewModel.SelectedSequence.Steps
                                        .FirstOrDefault(candidate => candidate.Step.Index == exactImpactIndex)?
                                        .Step.IsStartExact == true,
                                geometry = step.AttackGeometry,
                                target = step.AttackTarget,
                                targets = step.AttackTargets,
                            })
                            .ToArray(),
                        runtime = viewModel.RuntimeTraceAnalysis is null
                            ? null
                            : new
                            {
                                traceId = viewModel.SelectedRuntimeTrace?.Trace.TraceId,
                                traceName = viewModel.SelectedRuntimeTrace?.DisplayName,
                                sequenceRunId = viewModel.RuntimeTraceAnalysis.SequenceRunId,
                                runtimeStartFrame = viewModel.RuntimeTraceAnalysis.RuntimeStartFrame,
                                events = viewModel.RuntimeTraceAnalysis.Events
                                    .Select(projection => new
                                    {
                                        type = projection.Event.Type,
                                        frame = projection.SequenceFrame,
                                        stepIndex = projection.Event.StepIndex,
                                        skillId = projection.Event.SkillId,
                                        castIndex = projection.Event.CastIndex,
                                        castId = projection.Event.CastId,
                                        action = projection.Event.Action,
                                        key = projection.Event.Key,
                                        kind = projection.Event.Kind,
                                        reason = projection.Event.Reason,
                                        mode = projection.Event.Mode,
                                        projectileId = projection.Event.ProjectileId,
                                        preparedAttackId = projection.Event.PreparedAttackId,
                                        role = projection.Event.Role,
                                        durationFrames = projection.Event.DurationFrames,
                                        holdUntilCommit = projection.Event.HoldUntilCommit,
                                        targetCount = projection.Event.TargetCount,
                                        attackDirection = projection.Event.AttackDirection,
                                        chainCount = projection.Event.ChainCount,
                                        colliderIndex = projection.Event.ColliderIndex,
                                        radiusTiles = projection.Event.RadiusTiles,
                                        corridorWidthPixels = projection.Event.CorridorWidthPixels,
                                        colliderRadiusPixels = projection.Event.ColliderRadiusPixels,
                                        chainSpacingTiles = projection.Event.ChainSpacingTiles,
                                        chainReachTiles = projection.Event.ChainReachTiles,
                                        point = projection.Event.Point,
                                        center = projection.Event.Center,
                                        origin = projection.Event.Origin,
                                        end = projection.Event.End,
                                        player = projection.Event.Player,
                                        boss = projection.Event.Boss,
                                        owner = projection.Event.Owner,
                                        target = projection.Event.Target,
                                        geometry = projection.Event.Geometry,
                                        centers = projection.Event.Centers,
                                        targets = projection.Event.Targets,
                                    })
                                    .ToArray(),
                                comparisons = viewModel.RuntimeTraceAnalysis.Attacks
                                    .Select(comparison => new
                                    {
                                        comparison.SequenceRunId,
                                        comparison.StepIndex,
                                        comparison.SkillId,
                                        comparison.CastIndex,
                                        comparison.CastId,
                                        comparison.ExpectedStartFrame,
                                        comparison.ExpectedExecutionFrame,
                                        comparison.RuntimeStartFrame,
                                        comparison.RuntimeTelegraphFrame,
                                        comparison.RuntimeExecutionFrame,
                                        comparison.RuntimeColliderFrame,
                                        comparison.ExecutionDeltaFrames,
                                        comparison.TelegraphColliderOffsetTiles,
                                        comparison.ExpectedRadiusTiles,
                                        comparison.RuntimeRadiusTiles,
                                        comparison.RadiusDeltaTiles,
                                        comparison.ExpectedColliderRadiusPixels,
                                        comparison.RuntimeColliderRadiusPixels,
                                        comparison.ColliderRadiusDeltaPixels,
                                        comparison.ExpectedColliderCount,
                                        comparison.RuntimeColliderCount,
                                        comparison.ColliderCountDelta,
                                        comparison.ColliderPathOffsetTiles,
                                        comparison.AttackLifecycleId,
                                        comparison.IsExpectedExecutionExact,
                                        comparison.RuntimeCommitFrame,
                                        comparison.RuntimeMovementStartFrame,
                                        comparison.RuntimeMovementCompletedFrame,
                                        comparison.CommitExecutionDeltaFrames,
                                        comparison.LandingImpactDeltaFrames,
                                        status = comparison.Status.ToString(),
                                        comparison.Summary,
                                    })
                                    .ToArray(),
                            },
                    },
            },
            JsonOptions);
        core.PostWebMessageAsJson(payload);
        SendSchedulerFrame(false);
    }

    private void SendSchedulerFrame(bool activateTemporalMode)
    {
        var core = PreviewWebView.CoreWebView2;
        var viewModel = _viewModel;
        if (!_rendererReady || core is null || viewModel?.SelectedMap is null)
        {
            return;
        }
        core.PostWebMessageAsJson(JsonSerializer.Serialize(
            new
            {
                type = "setSchedulerFrame",
                frame = viewModel.PreviewFrame,
                activateTemporalMode,
            },
            JsonOptions));
    }

    private void ShowHostError(string message)
    {
        LoadingIndicator.IsActive = false;
        LoadingIndicator.Visibility = Visibility.Collapsed;
        HostStatus.Text = message;
    }
}
