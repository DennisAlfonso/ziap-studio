using System.Diagnostics;

namespace ZiapStudio.Services.ProjectSafety;

public interface IRpgMakerProcessProvider
{
    bool IsRpgMakerRunning();
}

public sealed class SystemRpgMakerProcessProvider : IRpgMakerProcessProvider
{
    public bool IsRpgMakerRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("RPGMZ");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}

public interface IRpgMakerProcessMonitor
{
    bool IsRunning { get; }

    event EventHandler<bool>? RunningChanged;

    void Refresh();
}

public sealed class RpgMakerProcessMonitor : IRpgMakerProcessMonitor
{
    private readonly IRpgMakerProcessProvider _provider;
    private bool _isRunning;

    public RpgMakerProcessMonitor(IRpgMakerProcessProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _isRunning = _provider.IsRpgMakerRunning();
    }

    public bool IsRunning => _isRunning;

    public event EventHandler<bool>? RunningChanged;

    public void Refresh()
    {
        var current = _provider.IsRpgMakerRunning();
        if (current == _isRunning)
        {
            return;
        }

        _isRunning = current;
        RunningChanged?.Invoke(this, current);
    }
}
