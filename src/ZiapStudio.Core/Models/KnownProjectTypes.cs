namespace ZiapStudio.Core.Models;

public static class KnownProjectTypes
{
    public const string Unknown = "unknown";
    public const string Web = "web";
    public const string RpgMakerMz = "rpgmaker-mz";
    public const string RpgMakerMv = "rpgmaker-mv";

    public static string GetDisplayName(string projectType) => projectType switch
    {
        RpgMakerMz => "RPG Maker MZ",
        RpgMakerMv => "RPG Maker MV",
        Web => "Web",
        Unknown or "" => "Non riconosciuto",
        _ => projectType,
    };
}
