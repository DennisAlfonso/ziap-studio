using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZiapStudio.Core.Fusion.Audio;

public sealed record FusionAudioCatalog
{
    public int SchemaVersion { get; init; } = 1;

    public double MasterVolume { get; init; } = 1;

    public IReadOnlyDictionary<string, FusionAudioCategory> Categories { get; init; } =
        new Dictionary<string, FusionAudioCategory>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FusionAudioEntry> Sounds { get; init; } =
        new Dictionary<string, FusionAudioEntry>(StringComparer.OrdinalIgnoreCase);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record FusionAudioCategory
{
    public double Volume { get; init; } = 1;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record FusionAudioEntry
{
    public string? Category { get; init; }

    public FusionAudioSource Source { get; init; } = new();

    public double Volume { get; init; } = 90;

    public FusionAudioPitch Pitch { get; init; } = new();

    public double Pan { get; init; }

    public int Cooldown { get; init; }

    public bool AntiRepeat { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record FusionAudioSource
{
    public string Kind { get; init; } = FusionAudioSourceKinds.Files;

    public string? Slot { get; init; }

    public IReadOnlyList<string> Files { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record FusionAudioPitch
{
    public double Min { get; init; } = 100;

    public double Max { get; init; } = 100;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public static class FusionAudioSourceKinds
{
    public const string Files = "files";
    public const string SystemSound = "systemSound";
}

public static class FusionAudioSystemSounds
{
    public static IReadOnlyList<string> Slots { get; } =
    [
        "cursor", "ok", "cancel", "buzzer", "equip", "save", "load",
        "battleStart", "escape", "enemyAttack", "enemyDamage", "enemyCollapse",
        "bossCollapse1", "bossCollapse2", "actorDamage", "actorCollapse",
        "recovery", "miss", "evasion", "magicEvasion", "reflection", "shop",
        "useItem", "useSkill",
    ];

    public static bool IsKnown(string? slot) =>
        Slots.Contains(slot ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}

public sealed record FusionAudioResolvedAsset
{
    public required string EventId { get; init; }

    public required string AssetName { get; init; }

    public required string RelativePath { get; init; }

    public string? ResolvedPath { get; init; }

    public string SourceKind { get; init; } = FusionAudioSourceKinds.Files;

    public string? SystemSlot { get; init; }

    public bool Exists { get; init; }
}

public sealed record FusionAudioFileOption
{
    public required string CatalogPath { get; init; }

    public required string ResolvedPath { get; init; }

    public IReadOnlyList<string> Formats { get; init; } = [];
}

public sealed record FusionAudioPlaybackPlan
{
    public required string EventId { get; init; }

    public required string AssetPath { get; init; }

    public required string RelativePath { get; init; }

    public double Volume { get; init; } = 1;

    public double Pitch { get; init; } = 100;

    public double PlaybackRate { get; init; } = 1;

    public double Pan { get; init; }

    public int CooldownMs { get; init; }

    public string? VariantId { get; init; }

    public int? VariantIndex { get; init; }
}

public enum FusionAudioPlaybackResolutionStatus
{
    Ready,
    Cooldown,
    Unresolved,
}

public sealed record FusionAudioPlaybackResolution
{
    public required FusionAudioPlaybackResolutionStatus Status { get; init; }

    public FusionAudioPlaybackPlan? Plan { get; init; }

    public int RemainingCooldownMs { get; init; }

    public string? Message { get; init; }

    public bool IsReady => Status == FusionAudioPlaybackResolutionStatus.Ready && Plan is not null;
}

public enum FusionAudioDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record FusionAudioDiagnostic
{
    public required string Code { get; init; }

    public required FusionAudioDiagnosticSeverity Severity { get; init; }

    public required string Message { get; init; }

    public string? EventId { get; init; }
}
