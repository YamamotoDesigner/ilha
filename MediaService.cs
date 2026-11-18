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
        public ImageSource? Art { get; private set; }
        public bool IsPlaying { get; private set; }
        public TimeSpan Duration { get; private set; }
        /// <summary>O app aceita aleatório / repetir pelos controles de mídia do Windows.</summary>
        public bool CanShuffle { get; private set; }
        public bool CanRepeat { get; private set; }
        public bool ShuffleOn { get; private set; }
        /// <summary>0 = não repete, 1 = repete a lista, 2 = repete a música.</summary>
        public int RepeatMode { get; private set; }

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
            _manager.CurrentSessionChanged += (_, _) => _dispatcher.InvokeAsync(() => _ = PickAsync());
            _manager.SessionsChanged += (_, _) => _dispatcher.InvokeAsync(() => _ = PickAsync());
            _pickTimer.Interval = TimeSpan.FromSeconds(1.5);
            _pickTimer.Tick += (_, _) => _ = PickAsync();
            _pickTimer.Start();
            await PickAsync();
        }

        // ───── Quais apps aparecem ─────

        private readonly DispatcherTimer _pickTimer = new();
        private bool _picking;
        private bool _onlyMainApps = true;
        /// <summary>Títulos já reconhecidos como YouTube (a aba pode ir para o fundo depois).</summary>
        private readonly System.Collections.Generic.HashSet<string> _youTubeTitles = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ligado (padrão): só Spotify e YouTube aparecem na ilha. Desligado: qualquer app com som.</summary>
        public bool OnlyMainApps
        {
            get => _onlyMainApps;
            set
            {
                if (_onlyMainApps == value) return;
                _onlyMainApps = value;
                if (_manager != null) _ = PickAsync();
            }
        }

        private static readonly string[] Browsers =
            { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "arc", "thorium", "zen", "librewolf", "floorp" };

        private static bool IsBrowser(string id)
        {
            string x = id.ToLowerInvariant();
            if (x.Length >= 12 && x.All(Uri.IsHexDigit)) return true; // Firefox usa um ID opaco
            return Browsers.Any(b => x.Contains(b));
        }

        /// <summary>
        /// O navegador não diz qual site está tocando. O título da janela diz ("Vídeo - YouTube - Google Chrome"):
        /// se alguma janela tem o título da mídia junto com "YouTube", é YouTube. Fica guardado para quando
        /// a aba for para o fundo.
        /// </summary>
        private bool IsYouTube(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            if (_youTubeTitles.Contains(title)) return true;
            foreach (string w in Native.WindowTitles())
            {
                if (!w.Contains("YouTube", StringComparison.OrdinalIgnoreCase)) continue;
                if (w.Contains(title, StringComparison.OrdinalIgnoreCase) ||
                    w.StartsWith("YouTube Music", StringComparison.OrdinalIgnoreCase))
                {
                    if (_youTubeTitles.Count > 200) _youTubeTitles.Clear();
                    _youTubeTitles.Add(title);
                    return true;
                }
            }
            return false;
        }

        private async Task<bool> AllowedAsync(GlobalSystemMediaTransportControlsSession s)
        {
            if (!_onlyMainApps) return true;
            string id = s.SourceAppUserModelId ?? "";
            if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return true;
            if (id.Contains("YouTube", StringComparison.OrdinalIgnoreCase)) return true; // apps/PWA do YouTube
            if (!IsBrowser(id)) return false;
            try
            {
                var props = await s.TryGetMediaPropertiesAsync();
                return IsYouTube(props?.Title ?? "");
            }
            catch
            {
                return false;
            }
        }

        private static bool Playing(GlobalSystemMediaTransportControlsSession s)
        {
            try { return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }

        /// <summary>
        /// Escolhe a sessão mostrada. Sem filtro: a que o Windows indica. Com filtro: entre Spotify e YouTube,
        /// a indicada pelo Windows se for uma delas e estiver tocando; senão a que estiver tocando; senão a primeira.
        /// </summary>
        private async Task PickAsync()
        {
            if (_manager == null || _picking) return;
            _picking = true;
            try
            {
                var current = _manager.GetCurrentSession();
                var chosen = current;
                if (_onlyMainApps)
                {
                    var allowed = new System.Collections.Generic.List<GlobalSystemMediaTransportControlsSession>();
                    foreach (var s in _manager.GetSessions())
                        if (await AllowedAsync(s)) allowed.Add(s);
                    bool IsCurrent(GlobalSystemMediaTransportControlsSession s) =>
                        current != null && s.SourceAppUserModelId == current.SourceAppUserModelId;
                    chosen = allowed.FirstOrDefault(s => IsCurrent(s) && Playing(s))
                             ?? allowed.FirstOrDefault(Playing)
                             ?? allowed.FirstOrDefault(s => _session != null && s.SourceAppUserModelId == _session.SourceAppUserModelId)
                             ?? allowed.FirstOrDefault();
                }
                if (!SameSession(chosen, _session)) Attach(chosen);
            }
            catch
            {
                // A lista de sessões mudou no meio da leitura: tenta de novo no próximo ciclo.
            }
            finally
            {
                _picking = false;
            }
        }

        private static bool SameSession(GlobalSystemMediaTransportControlsSession? a, GlobalSystemMediaTransportControlsSession? b)
        {
            if (a == null || b == null) return a == b;
            return ReferenceEquals(a, b) || a.SourceAppUserModelId == b.SourceAppUserModelId;
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
                var info = _session?.GetPlaybackInfo();
                IsPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                CanShuffle = info?.Controls?.IsShuffleEnabled == true;
                CanRepeat = info?.Controls?.IsRepeatEnabled == true;
                ShuffleOn = info?.IsShuffleActive == true;
                RepeatMode = info?.AutoRepeatMode switch
                {
                    Windows.Media.MediaPlaybackAutoRepeatMode.Track => 2,
                    Windows.Media.MediaPlaybackAutoRepeatMode.List => 1,
                    _ => 0,
                };
            }
            catch
            {
                IsPlaying = false;
                CanShuffle = CanRepeat = false;
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
                if (_positionAt > DateTimeOffset.Now) _positionAt = DateTimeOffset.Now;
            }
            catch
            {
                Duration = TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Confere a posição com o app uma vez por segundo. Alguns apps só avisam a posição de vez em
        /// quando; se a estimativa local se afastou mais de meio segundo, volta a seguir o app.
        /// </summary>
        public void Resync()
        {
            try
            {
                var t = _session?.GetTimelineProperties();
                if (t == null || !IsPlaying) return;
                var updated = t.LastUpdatedTime.Year < 2000 ? DateTimeOffset.Now : t.LastUpdatedTime;
                if (updated > DateTimeOffset.Now) updated = DateTimeOffset.Now;
                var reported = (t.Position - t.StartTime) + (DateTimeOffset.Now - updated);
                if (Math.Abs((reported - Position).TotalSeconds) > 0.5)
                {
                    _position = t.Position - t.StartTime;
                    _positionAt = updated;
                    Duration = t.EndTime - t.StartTime;
                }
            }
            catch
            {
                // Sessão fechou no meio da leitura.
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

        public async Task<bool> SetShuffleAsync(bool on)
        {
            try { return _session != null && await _session.TryChangeShuffleActiveAsync(on); } catch { return false; }
        }

        /// <summary>0 = não repete, 1 = lista, 2 = música.</summary>
        public async Task<bool> SetRepeatAsync(int mode)
        {
            try
            {
                var m = mode switch
                {
                    2 => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                    1 => Windows.Media.MediaPlaybackAutoRepeatMode.List,
                    _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
                };
                return _session != null && await _session.TryChangeAutoRepeatModeAsync(m);
            }
            catch
            {
                return false;
            }
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
