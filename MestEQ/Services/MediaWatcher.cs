using System;
using System.Linq;
using System.Threading.Tasks;
using MestEQ.Core;
using Windows.Media.Control;

namespace MestEQ.Services
{
    /// <summary>
    /// Windows'un medya oturumlarından (Spotify, tarayıcı, vb.) çalan şarkıyı okur.
    /// Spotify açıksa her zaman onu tercih eder.
    /// </summary>
    public sealed class MediaWatcher
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private string _sessionId = "";

        public NowPlaying Current { get; private set; } = NowPlaying.Empty;

        /// <summary>Şarkı değişti; arka plan iş parçacığından gelebilir.</summary>
        public event Action<NowPlaying>? Changed;

        public async Task StartAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.SessionsChanged += (_, _) => _ = PickSessionAsync();
                _manager.CurrentSessionChanged += (_, _) => _ = PickSessionAsync();
                await PickSessionAsync();
            }
            catch
            {
                // Medya oturumlarına erişilemiyorsa otomatik geçiş devre dışı kalır; uygulama çalışmaya devam eder.
            }
        }

        private async Task PickSessionAsync()
        {
            if (_manager == null) return;
            GlobalSystemMediaTransportControlsSession? chosen = null;
            try
            {
                var sessions = _manager.GetSessions();
                chosen = sessions.FirstOrDefault(s => (s.SourceAppUserModelId ?? "").Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                         ?? _manager.GetCurrentSession();
            }
            catch { chosen = null; }

            string id = chosen?.SourceAppUserModelId ?? "";
            if (id != _sessionId || (chosen == null) != (_session == null))
            {
                if (_session != null)
                {
                    try { _session.MediaPropertiesChanged -= OnPropertiesChanged; } catch { }
                }
                _session = chosen;
                _sessionId = id;
                if (_session != null)
                {
                    try { _session.MediaPropertiesChanged += OnPropertiesChanged; } catch { }
                }
            }
            await ReadAsync();
        }

        private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            _ = ReadAsync(250);
        }

        private async Task ReadAsync(int delayMs = 0)
        {
            try
            {
                // Spotify şarkı değişiminde önce boş, sonra dolu bilgi gönderebiliyor; kısa bir bekleme bunu yumuşatır.
                if (delayMs > 0) await Task.Delay(delayMs);
                var session = _session;
                if (session == null) { Publish(NowPlaying.Empty); return; }
                var p = await session.TryGetMediaPropertiesAsync();
                if (p == null) return;
                var np = new NowPlaying(p.Title ?? "", p.Artist ?? "", p.AlbumTitle ?? "", SourceName(_sessionId));
                if (np.IsEmpty) return;
                Publish(np);
            }
            catch { /* oturum kapanmış olabilir */ }
        }

        private void Publish(NowPlaying np)
        {
            if (np == Current) return;
            Current = np;
            Changed?.Invoke(np);
        }

        private static string SourceName(string appId)
        {
            if (appId.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
            if (appId.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
            if (appId.Contains("msedge", StringComparison.OrdinalIgnoreCase)) return "Edge";
            if (appId.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
            if (appId.Length == 0) return "";
            var name = appId.Split('!', '\\', '.').FirstOrDefault(s => s.Length > 0) ?? appId;
            return name;
        }
    }
}
