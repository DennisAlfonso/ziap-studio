namespace ZiapStudio.Core.Editing;

public sealed record DocumentSourceSnapshot
{
    public string SourcePath { get; init; } = string.Empty;

    public DateTimeOffset LoadedAtUtc { get; init; }

    public DateTimeOffset LastWriteTimeUtc { get; init; }

    public long Length { get; init; }

    public string ContentHash { get; init; } = string.Empty;
}
