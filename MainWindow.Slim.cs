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
    }
}
