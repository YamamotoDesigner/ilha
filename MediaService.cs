using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Ilha
{
    /// <summary>
    /// Lê a mídia em reprodução (Spotify, navegadores, players) pela API de controles de
    /// mídia do Windows — a mesma usada pelo painel de volume do sistema.
    /// </summary>
    public sealed class MediaService
    {
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private int _version;
        private TimeSpan _position;
        private DateTimeOffset _positionAt;
        private TimeSpan _start;

        public event Action? Changed;

        public bool HasSession => _session != null;
        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public string SourceApp { get; private set; } = "";
        /// <summary>Identificador bruto do app (ex.: "chrome.exe", "MSEdge").</summary>
        public string SourceAppId { get; private set; } = "";

        /// <summary>A mídia vem de um navegador (YouTube e afins).</summary>
        public bool FromBrowser
        {
            get
            {
                string id = SourceAppId.ToLowerInvariant();
                return new[] { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc" }.Any(id.Contains)
                       || (id.Length > 0 && SourceApp.Length == 0); // Firefox usa um ID opaco
            }
        }
        public ImageSource? Art { get; private set; }
        public bool IsPlaying { get; private set; }
        public TimeSpan Duration { get; private set; }

        public TimeSpan Position
        {
            get
            {
                var p = _position;
                if (IsPlaying) p += DateTimeOffset.Now - _positionAt;
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p < TimeSpan.Zero ? TimeSpan.Zero : p;
            }
        }

        public async Task InitAsync()
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (m, _) => _dispatcher.InvokeAsync(() => Attach(m.GetCurrentSession()));
            Attach(_manager.GetCurrentSession());
        }

        private void Attach(GlobalSystemMediaTransportControlsSession? session)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
                _session.TimelinePropertiesChanged -= OnTimelineChanged;
            }

            _session = session;
            _version++;

            if (session != null)
            {
                session.MediaPropertiesChanged += OnPropertiesChanged;
                session.PlaybackInfoChanged += OnPlaybackChanged;
                session.TimelinePropertiesChanged += OnTimelineChanged;
            }
            else
            {
                Title = Artist = SourceApp = SourceAppId = "";
                Art = null;
            }

            ReadPlayback();
            ReadTimeline();
            Changed?.Invoke();
            _ = RefreshPropertiesAsync();
        }

        private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
            _dispatcher.InvokeAsync(() => _ = RefreshPropertiesAsync());

        private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
            _dispatcher.InvokeAsync(() => { ReadPlayback(); ReadTimeline(); Changed?.Invoke(); });

        private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) =>
            _dispatcher.InvokeAsync(() => { ReadTimeline(); Changed?.Invoke(); });

        private void ReadPlayback()
        {
            try
            {
                IsPlaying = _session?.GetPlaybackInfo()?.PlaybackStatus ==
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch
            {
                IsPlaying = false;
            }
        }

        private void ReadTimeline()
        {
            try
            {
                var t = _session?.GetTimelineProperties();
                if (t == null)
                {
                    Duration = TimeSpan.Zero;
                    _position = TimeSpan.Zero;
                    return;
                }
                _start = t.StartTime;
                Duration = t.EndTime - t.StartTime;
                _position = t.Position - t.StartTime;
                _positionAt = t.LastUpdatedTime.Year < 2000 ? DateTimeOffset.Now : t.LastUpdatedTime;
            }
            catch
            {
                Duration = TimeSpan.Zero;
            }
        }

        private async Task RefreshPropertiesAsync()
        {
            var session = _session;
            int version = ++_version;
            if (session == null) return;

            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                ImageSource? art = props?.Thumbnail != null ? await LoadArtAsync(props.Thumbnail) : null;
                if (version != _version) return; // chegou um resultado mais novo

                Title = props?.Title ?? "";
                Artist = !string.IsNullOrEmpty(props?.Artist) ? props!.Artist : props?.AlbumArtist ?? "";
                SourceAppId = session.SourceAppUserModelId ?? "";
                SourceApp = PrettyAppName(session.SourceAppUserModelId);
                Art = art;
                Changed?.Invoke();
            }
            catch
            {
                // Alguns apps fecham a sessão no meio da leitura.
            }
        }

        private static async Task<ImageSource?> LoadArtAsync(IRandomAccessStreamReference reference)
        {
            try
            {
                using var ras = await reference.OpenReadAsync();
                using var stream = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 240;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private static string PrettyAppName(string? id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string x = id;
            int bang = x.LastIndexOf('!');
            if (bang >= 0) x = x[(bang + 1)..];
            if (x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) x = x[..^4];
            int dot = x.LastIndexOf('.');
            if (dot >= 0 && dot < x.Length - 1) x = x[(dot + 1)..];
            if (x.Length >= 12 && x.All(Uri.IsHexDigit)) return ""; // IDs opacos (ex.: Firefox)
            return x switch
            {
                "MSEdge" => "Edge",
                "ZuneMusic" => "Media Player",
                _ => x,
            };
        }

        public async Task TogglePlayPauseAsync()
        {
            try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { }
        }

        /// <summary>Pula para um ponto da faixa (nem todo app aceita).</summary>
        public async Task SeekAsync(TimeSpan position)
        {
            try
            {
                if (_session == null || Duration <= TimeSpan.Zero) return;
                if (position < TimeSpan.Zero) position = TimeSpan.Zero;
                if (position > Duration) position = Duration;
                if (await _session.TryChangePlaybackPositionAsync((_start + position).Ticks))
                {
                    _position = position;
                    _positionAt = DateTimeOffset.Now;
                    Changed?.Invoke();
                }
            }
            catch { }
        }

        public async Task PauseAsync()
        {
            try { if (_session != null && IsPlaying) await _session.TryPauseAsync(); } catch { }
        }

        public async Task NextAsync()
        {
            try { if (_session != null) await _session.TrySkipNextAsync(); } catch { }
        }

        public async Task PreviousAsync()
        {
            try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { }
        }
    }
}
