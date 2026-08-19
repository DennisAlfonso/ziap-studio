using ZiapStudio.Core.Fusion.Audio;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Fusion.Audio;

public interface IFusionAudioRandomSource
{
    double NextDouble();
}

public sealed class FusionAudioRandomSource : IFusionAudioRandomSource
{
    public double NextDouble() => Random.Shared.NextDouble();
}

public sealed class FusionAudioPlaybackResolver
{
    private readonly FusionAudioCatalogService _catalogService;
    private readonly IFusionAudioRandomSource _random;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, DateTimeOffset> _lastPlayedAt =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lastVariantIndex =
        new(StringComparer.OrdinalIgnoreCase);

    public FusionAudioPlaybackResolver(
        FusionAudioCatalogService catalogService,
        IFusionAudioRandomSource? random = null,
        TimeProvider? timeProvider = null)
    {
        _catalogService = catalogService;
        _random = random ?? new FusionAudioRandomSource();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<FusionAudioPlaybackResolution> ResolveAsync(
        ZiapProject project,
        FusionAudioCatalog catalog,
        string eventId,
        FusionAudioEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentNullException.ThrowIfNull(entry);

        var stateKey = $"{Path.GetFullPath(project.Path)}|{eventId.Trim()}";
        var cooldown = Math.Max(0, entry.Cooldown);
        var now = _timeProvider.GetUtcNow();
        if (cooldown > 0 && _lastPlayedAt.TryGetValue(stateKey, out var previous))
        {
            var elapsed = (int)Math.Floor((now - previous).TotalMilliseconds);
            if (elapsed < cooldown)
            {
                return new FusionAudioPlaybackResolution
                {
                    Status = FusionAudioPlaybackResolutionStatus.Cooldown,
                    RemainingCooldownMs = cooldown - Math.Max(0, elapsed),
                    Message = "La chiamata è stata soppressa dal cooldown dell'evento.",
                };
            }
        }

        var sourceKind = entry.Source.Kind?.Trim() ?? string.Empty;
        string? variantId = null;
        int? selectedVariantIndex = null;
        var previewEntry = entry;
        if (sourceKind.Equals(FusionAudioSourceKinds.Files, StringComparison.OrdinalIgnoreCase))
        {
            var variants = entry.Source.Files
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (variants.Length == 0)
            {
                return Unresolved("L'evento non contiene varianti audio riproducibili.");
            }

            var index = SelectVariantIndex(stateKey, variants.Length, entry.AntiRepeat);
            selectedVariantIndex = index;
            variantId = variants[index];
            previewEntry = entry with
            {
                Source = entry.Source with { Files = [variantId] },
            };
        }

        var asset = await _catalogService.ResolvePreviewAsync(
            project,
            eventId,
            previewEntry,
            cancellationToken);
        if (asset is not { Exists: true, ResolvedPath: not null })
        {
            return Unresolved("La sorgente dell'evento non esiste o non può essere risolta.");
        }

        variantId ??= asset.AssetName;
        var category = entry.Category?.Trim() ?? string.Empty;
        var categoryVolume = catalog.Categories.TryGetValue(category, out var categoryDefinition)
            ? Math.Clamp(categoryDefinition.Volume, 0, 1)
            : 1;
        var pitch = SelectPitch(entry.Pitch);
        var plan = new FusionAudioPlaybackPlan
        {
            EventId = eventId,
            AssetPath = asset.ResolvedPath,
            RelativePath = asset.RelativePath,
            Volume = Math.Clamp(
                Math.Clamp(catalog.MasterVolume, 0, 1) *
                categoryVolume *
                (Math.Clamp(entry.Volume, 0, 100) / 100.0),
                0,
                1),
            Pitch = pitch,
            PlaybackRate = pitch / 100.0,
            Pan = Math.Clamp(entry.Pan, -100, 100) / 100.0,
            CooldownMs = cooldown,
            VariantId = variantId,
            VariantIndex = selectedVariantIndex,
        };

        return new FusionAudioPlaybackResolution
        {
            Status = FusionAudioPlaybackResolutionStatus.Ready,
            Plan = plan,
        };
    }

    public void CommitPlayback(ZiapProject project, FusionAudioPlaybackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(plan);
        var stateKey = $"{Path.GetFullPath(project.Path)}|{plan.EventId.Trim()}";
        _lastPlayedAt[stateKey] = _timeProvider.GetUtcNow();
        if (plan.VariantIndex is int selectedIndex)
        {
            _lastVariantIndex[stateKey] = selectedIndex;
        }
    }

    public void ResetSession()
    {
        _lastPlayedAt.Clear();
        _lastVariantIndex.Clear();
    }

    private int SelectVariantIndex(string stateKey, int count, bool antiRepeat)
    {
        if (count <= 1) return 0;
        var index = RandomIndex(count);
        if (antiRepeat && _lastVariantIndex.TryGetValue(stateKey, out var previous) &&
            previous == index)
        {
            index = (index + 1 + RandomIndex(count - 1)) % count;
        }
        return index;
    }

    private double SelectPitch(FusionAudioPitch pitch)
    {
        var minimum = Math.Clamp(pitch.Min, 50, 150);
        var maximum = Math.Clamp(pitch.Max, 50, 150);
        if (minimum > maximum)
        {
            (minimum, maximum) = (maximum, minimum);
        }
        if (Math.Abs(minimum - maximum) < double.Epsilon) return minimum;
        if (IsInteger(minimum) && IsInteger(maximum))
        {
            return Math.Min(
                maximum,
                minimum + Math.Floor(_random.NextDouble() * (maximum - minimum + 1)));
        }
        return minimum + (_random.NextDouble() * (maximum - minimum));
    }

    private int RandomIndex(int count) => Math.Min(
        count - 1,
        (int)Math.Floor(_random.NextDouble() * count));

    private static bool IsInteger(double value) => Math.Abs(value % 1) < double.Epsilon;

    private static FusionAudioPlaybackResolution Unresolved(string message) => new()
    {
        Status = FusionAudioPlaybackResolutionStatus.Unresolved,
        Message = message,
    };
}
