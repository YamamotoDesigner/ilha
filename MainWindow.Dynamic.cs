using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Ilha
{
    /// <summary>
    /// Ilha dinâmica: nas abas de listas (copiados, tarefas, lembretes, páginas), a altura acompanha
    /// quantos itens existem — cresce ao chegar um item novo e encolhe ao apagar.
    /// </summary>
    public partial class MainWindow
    {
        private bool _dynamicHeight;
        private bool _resizeQueued;

        /// <summary>Altura que a página pede (sem o cabeçalho), ou null para as de tamanho fixo.</summary>
        private double? DynamicPageHeight(string tab)
        {
            const int maxRows = 6;
            switch (tab)
            {
                case "clipboard":
                {
                    int n = _clips.Count;
                    // Linha de status (30) + itens de 38 px; vazio: o aviso "Nada copiado".
                    return n == 0 ? 96 : 30 + Math.Min(n, maxRows) * 38 - 5;
                }
                case "tasks":
                {
                    int n = _tasks.Count;
                    // Cartão (21) + título (21) + barra (10) + campo (34) + linhas de 24.
                    return 94 + (n == 0 ? 34 : Math.Min(n, maxRows) * 26);
                }
                case "agenda":
                {
                    int n = _todos.Count(t => TodoVisible(t));
                    // Faixa de dias (68) + cartão com título, campo e lembretes de 23 px.
                    return 68 + 78 + (n == 0 ? 24 : Math.Min(n, 5) * 25);
                }
                case "notes":
                {
                    if (_openNote != null) return null; // página aberta: tamanho fixo para escrever
                    int n = _settings.NotePages.Count;
                    int cols = NotePagesScroll.ActualWidth >= 330 || NotePagesScroll.ActualWidth <= 0 ? 2 : 1;
                    int rows = (int)Math.Ceiling(n / (double)cols);
                    return n == 0 ? 120 : 34 + Math.Min(rows, 3) * 64;
                }
                default:
                    return null;
            }
        }

        /// <summary>A lista mudou: ajusta a altura da ilha (uma vez por quadro, com animação).</summary>
        private void QueueDynamicResize()
        {
            if (_resizeQueued || InOverview || DynamicPageHeight(_currentTab) == null && !_dynamicHeight) return;
            _resizeQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                _resizeQueued = false;
                ApplyTabSize(animate: true);
            }, DispatcherPriority.Background);
        }

        private void ClipRemove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ClipItem item) _clips.Remove(item);
        }
    }
}
