namespace ZiapStudio.Services.Localization;

public interface ILocalizationCacheInvalidator
{
    void Invalidate(string projectPath, string locale, string sourceFile);
}
