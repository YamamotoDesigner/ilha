using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ilha
{
    /// <summary>
    /// Ilha dinâmica: quando uma janela de app (navegador, editor…) está de fato embaixo da ilha,
    /// com os botões e abas do topo ali, a ilha recolhida vira só um risco no topo da tela. Assim
    /// que essa janela é minimizada, movida para baixo ou fechada, a ilha volta ao tamanho normal.
    /// Passar o mouse no risco abre a ilha como sempre; avisos continuam aparecendo.
    /// </summary>
    public partial class MainWindow
    {
        private const double SlimHeight = 5;
        private bool _appInFront;
        private IntPtr _foregroundHook, _windowHook;
        private Native.WinEventProc? _foregroundCallback; // referência viva: o Windows chama de volta
        private readonly DispatcherTimer _catchTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private readonly DispatcherTimer _underTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private bool _refreshQueued;

        private DateTime _slimHoldUntil;

        private bool SlimWanted => _settings.AutoSlim && _appInFront && !_timerAlerting && DateTime.Now >= _slimHoldUntil;

        /// <summary>
        /// Logo depois da apresentação, a ilha fica do tamanho em que pousou por alguns segundos
        /// (a troca para a ilha de verdade não aparece); depois, se houver janela embaixo, vira risco.
        /// </summary>
        public void HoldFullSize(double seconds)
        {
            _slimHoldUntil = DateTime.Now.AddSeconds(seconds);
            var release = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(50) };
            release.Tick += (_, _) =>
            {
                release.Stop();
                UpdateSlimWatch();
                UpdateCollapsedState(force: true);
            };
            release.Start();
        }

        private double SlimWidth => Math.Clamp(_settings.CollapsedWidth * 0.5, 56, 120);

        private void InitSlim()
        {
            _foregroundCallback = (_, _, _, _, _, _, _) =>
            {
                if (_refreshQueued) return;
                _refreshQueued = true;
                Dispatcher.BeginInvoke(() => { _refreshQueued = false; RefreshForeground(); }, DispatcherPriority.Background);
            };
            _foregroundHook = Native.WatchForeground(_foregroundCallback);
            _windowHook = Native.WatchWindowChanges(_foregroundCallback);
            _catchTimer.Tick += (_, _) => UpdateDropCatch();
            // Reserva: janelas maximizadas por atalho, arrastadas etc. nem sempre avisam.
            _underTimer.Tick += (_, _) => RefreshForeground();
            _underTimer.Start();
            RefreshForeground();
        }

        /// <summary>
        /// Confere se há uma janela de app embaixo da ilha (risco) ou não (ilha normal).
        /// Olha só a faixa do topo onde a ilha fica, no monitor dela.
        /// </summary>
        private void RefreshForeground()
        {
            if (_hwnd == IntPtr.Zero) return;
            bool under = false;
            if (_settings.AutoSlim)
            {
                var bounds = TargetScreen().Bounds;
                var dpi = VisualTreeHelper.GetDpi(this);
                int center = Native.WindowRect(_hwnd, out var r) && r.Right > r.Left
                    ? (r.Left + r.Right) / 2
                    : bounds.Left + bounds.Width / 2;
                int half = (int)Math.Round(Math.Max(_settings.CollapsedWidth / 2 + 24, 110) * dpi.DpiScaleX);
                var strip = new Native.RECT
                {
                    Left = Math.Max(bounds.Left, center - half),
                    Right = Math.Min(bounds.Right, center + half),
                    Top = bounds.Top,
                    Bottom = bounds.Top + (int)Math.Round(36 * dpi.DpiScaleY),
                };
                under = Native.IsAppUnder(strip);
            }
            if (under == _appInFront) return;
            _appInFront = under;
            UpdateSlimWatch();
            UpdateCollapsedState(force: true);
        }


        private void UpdateSlimWatch()
        {
            if (SlimWanted)
            {
                _catchTimer.Start();
            }
            else
            {
                _catchTimer.Stop();
                DropCatch.Visibility = Visibility.Collapsed;
            }
            UpdateSlimWaves();
        }

        /// <summary>
        /// Acertar um risco de 5 px arrastando um arquivo seria difícil. Enquanto o botão do mouse
        /// está apertado perto da ilha, uma área invisível em volta dela passa a aceitar o arquivo
        /// (e a ilha abre embaixo do cursor). Solto o botão, a área some e não atrapalha os cliques.
        /// </summary>
        private void UpdateDropCatch()
        {
            bool on = false;
            if (SlimWanted && !_expanded && !_hiddenForFullscreen && !_draggingOut &&
                Native.LeftButtonDown && Native.WindowRect(_hwnd, out var r))
            {
                var p = Native.CursorPosition();
                var dpi = VisualTreeHelper.GetDpi(this);
                double center = (r.Left + r.Right) / 2.0;
                on = Math.Abs(p.X - center) < 260 * dpi.DpiScaleX &&
                     p.Y >= r.Top - 4 && p.Y - r.Top < 160 * dpi.DpiScaleY;
            }
            var visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (DropCatch.Visibility != visibility) DropCatch.Visibility = visibility;
        }

        // ───────────── Ondas da música dentro do risco ─────────────

        private System.Windows.Shapes.Rectangle[] _waveBars = Array.Empty<System.Windows.Shapes.Rectangle>();
        private bool _wavesOn;
        private readonly System.Diagnostics.Stopwatch _waveClock = new();
        private Brush _waveInk = new SolidColorBrush(Color.FromArgb(0xE0, 255, 255, 255));

        private bool WavesWanted =>
            SlimWanted && !_expanded && !_peeking && !_alertShowing && !_nudging && !_hiddenForFullscreen &&
            _settings.ShowMedia && _media.HasSession && _media.IsPlaying;

        /// <summary>Cor das ondas: clara na ilha escura, escura na ilha clara.</summary>
        private void SetWaveInk(Color island)
        {
            bool light = island.R * 0.3 + island.G * 0.59 + island.B * 0.11 > 150;
            _waveInk = new SolidColorBrush(light ? Color.FromArgb(0xB8, 0, 0, 0) : Color.FromArgb(0xE6, 255, 255, 255));
            _waveInk.Freeze();
            foreach (var bar in _waveBars) bar.Fill = _waveInk;
        }

        private void UpdateSlimWaves()
        {
            bool want = WavesWanted;
            if (want == _wavesOn) return;
            _wavesOn = want;
            if (want)
            {
                _waveClock.Restart();
                CompositionTarget.Rendering -= DrawSlimWaves;
                CompositionTarget.Rendering += DrawSlimWaves;
                Fade(SlimWaves, 1, 420, delayMs: 260);
            }
            else
            {
                Fade(SlimWaves, 0, 160);
                var stop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                stop.Tick += (_, _) =>
                {
                    stop.Stop();
                    if (!_wavesOn) CompositionTarget.Rendering -= DrawSlimWaves;
                };
                stop.Start();
            }
        }

        /// <summary>
        /// Barrinhas finas que sobem e descem como uma onda de som, sempre dentro do risco:
        /// mais altas no meio, sumindo nas pontas, com um "pulso" leve como batida.
        /// </summary>
        private void DrawSlimWaves(object? sender, EventArgs e)
        {
            if (_wavesOn && !WavesWanted) { UpdateSlimWaves(); return; }
            double w = SlimWaves.ActualWidth, h = SlimWaves.ActualHeight;
            if (w < 12 || h < 2) return;

            const double barW = 1.5, step = 2.5;
            double inset = Math.Min(10, w * 0.12);
            int count = Math.Max(3, (int)((w - inset * 2) / step));
            if (_waveBars.Length != count)
            {
                SlimWaves.Children.Clear();
                _waveBars = new System.Windows.Shapes.Rectangle[count];
                for (int i = 0; i < count; i++)
                {
                    var bar = new System.Windows.Shapes.Rectangle { Width = barW, Fill = _waveInk, RadiusX = 0.8, RadiusY = 0.8 };
                    _waveBars[i] = bar;
                    SlimWaves.Children.Add(bar);
                }
            }

            double t = _waveClock.Elapsed.TotalSeconds;
            double start = (w - (count - 1) * step - barW) / 2;
            double beat = 0.72 + 0.28 * Math.Pow(Math.Abs(Math.Sin(t * 3.3)), 3);
            double max = h - 1.2;
            for (int i = 0; i < count; i++)
            {
                double u = count == 1 ? 0.5 : i / (double)(count - 1);
                double env = Math.Pow(Math.Sin(Math.PI * u), 0.7);
                double wave = 0.5 + 0.5 * (0.55 * Math.Sin(u * 13 - t * 5.4) +
                                           0.30 * Math.Sin(u * 29 + t * 3.2) +
                                           0.15 * Math.Sin(u * 6 - t * 8.1));
                double bh = Math.Max(0.8, max * env * (0.25 + 0.75 * wave) * beat);
                var bar = _waveBars[i];
                bar.Height = bh;
                System.Windows.Controls.Canvas.SetLeft(bar, start + i * step);
                System.Windows.Controls.Canvas.SetTop(bar, (h - bh) / 2);
            }
        }
    }
}
