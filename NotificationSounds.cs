using System.IO;
using System.Media;

namespace Mailcario;

internal static class NotificationSounds
{
    private static SoundPlayer? _player;
    private static readonly object Gate = new();

    internal static void Play()
    {
        try
        {
            lock (Gate)
            {
                if (_player is null)
                {
                    string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds",
                        "mailcario-cristal.wav");
                    var player = new SoundPlayer(path);
                    player.Load();
                    _player = player;
                }
                _player.Play();
            }
        }
        catch (Exception)
        {
            // Audio errors must never interrupt mail polling.
        }
    }
}
