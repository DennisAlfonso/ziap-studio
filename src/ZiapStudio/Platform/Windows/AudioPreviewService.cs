using Windows.Media.Core;
using Windows.Media.Playback;
using ZiapStudio.Core.Fusion.Audio;

namespace ZiapStudio.Platform.Windows;

public sealed class AudioPreviewService : IDisposable
{
    private readonly MediaPlayer _player = new()
    {
        AutoPlay = false,
    };

    public void Play(FusionAudioPlaybackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        PlayCore(plan.AssetPath, plan.Volume, plan.PlaybackRate, plan.Pan);
    }

    public void PlayRaw(string path) => PlayCore(path, volume: 1, playbackRate: 1, pan: 0);

    private void PlayCore(string path, double volume, double playbackRate, double pan)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("L'asset audio non esiste.", path);
        }

        var sourceUri = new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Path = Path.GetFullPath(path),
        }.Uri;
        _player.Source = MediaSource.CreateFromUri(sourceUri);
        _player.Volume = Math.Clamp(volume, 0, 1);
        _player.AudioBalance = Math.Clamp(pan, -1, 1);
        _player.PlaybackSession.PlaybackRate = Math.Clamp(playbackRate, 0.5, 1.5);
        _player.Play();
    }

    public void Stop()
    {
        _player.Pause();
        _player.PlaybackSession.Position = TimeSpan.Zero;
    }

    public void Dispose() => _player.Dispose();
}
