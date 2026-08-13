using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace ZiapStudio.Platform.Windows;

public sealed class WindowsFolderPickerService
{
    private readonly WindowId _windowId;

    public WindowsFolderPickerService(WindowId windowId)
    {
        _windowId = windowId;
    }

    public async Task<string?> PickProjectFolderAsync()
    {
        var picker = new FolderPicker(_windowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = "Apri progetto",
            ViewMode = PickerViewMode.List,
        };

        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }
}
