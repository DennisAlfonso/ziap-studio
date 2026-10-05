using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ZiapStudio.Converters;

/// <summary>Renders RPG Maker command indentation without coupling the semantic core to WinUI.</summary>
public sealed class StoryIndentationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var depth = value is int indent ? Math.Clamp(indent, 0, 6) : 0;
        return new Thickness(depth * 14, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => 0;
}
