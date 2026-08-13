using System.Diagnostics;
using ZiapStudio.Services.Integration.Console;

namespace ZiapStudio.Platform.Windows;

public sealed class WindowsShellService : IExternalUriLauncher
{
    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add(path);
        Process.Start(startInfo);
    }

    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri ||
            uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("L'URL deve essere HTTP o HTTPS e assoluto.", nameof(uri));
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        });
    }
}
