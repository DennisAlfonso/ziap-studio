namespace ZiapStudio.Services.Integration.Console;

public enum ConsoleNavigationArea
{
    Localization,
    Missions,
    Releases,
    Project,
}

public sealed record ConsoleNavigationTarget(
    string ProjectId,
    ConsoleNavigationArea Area,
    string? Language = null,
    string? File = null,
    string? Focus = null,
    string Source = "studio");
