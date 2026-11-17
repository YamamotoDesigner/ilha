using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ilha
{
    /// <summary>
    /// Ilha discreta: com um app na frente (navegador, editor…), a ilha recolhida vira só um risco
    /// escuro no topo da tela. Na área de trabalho ela fica do tamanho normal. Passar o mouse no
    /// risco abre a ilha como sempre; avisos (volume, música nova, lembretes) continuam aparecendo.
    /// </summary>
    public partial class MainWindow
    {
        private const double SlimHeight = 5;
        private bool _appInFront;
        private IntPtr _foregroundHook;
        private Native.WinEventProc? _foregroundCallback; // referência viva: o Windows chama de volta
        private readonly DispatcherTimer _catchTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

        private bool SlimWanted => _settings.AutoSlim && _appInFront && !_timerAlerting;

        private double SlimWidth => Math.Clamp(_settings.CollapsedWidth * 0.5, 56, 120);

        private void InitSlim()
        {
            _foregroundCallback = (_, _, _, _, _, _, _) =>
                Dispatcher.BeginInvoke(RefreshForeground, DispatcherPriority.Background);
            _foregroundHook = Native.WatchForeground(_foregroundCallback);
            _catchTimer.Tick += (_, _) => UpdateDropCatch();
            RefreshForeground();
        }

        /// <summary>Confere quem está na frente: área de trabalho (ilha normal) ou um app (risco).</summary>
        private void RefreshForeground()
        {
            if (Native.IsDesktopInFront() is not bool desktop || desktop == !_appInFront) return;
            _appInFront = !desktop;
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
