using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;

namespace Ilha
{
    /// <summary>
    /// Descobre qual vídeo do YouTube está aberto: lê a barra de endereço dos navegadores pela
    /// acessibilidade do Windows (UI Automation) ou, em último caso, um link copiado.
    /// </summary>
    internal static class YouTubeLocator
    {
        private static readonly Regex VideoId = new(
            @"(?:youtube\.com/(?:watch\?(?:[^#\s]*&)?v=|shorts/|live/|embed/)|youtu\.be/)([A-Za-z0-9_-]{11})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc" };

        public static string? ExtractId(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = VideoId.Match(text);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Link do YouTube na área de transferência (chamar na thread da interface).</summary>
        public static string? FromClipboard()
        {
            try
            {
                return Clipboard.ContainsText() ? ExtractId(Clipboard.GetText()) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Procura o vídeo nas janelas dos navegadores. A janela cujo título contém <paramref name="mediaTitle"/>
        /// (a aba que está tocando) é verificada primeiro. Pode levar algumas centenas de ms: use fora da UI.
        /// </summary>
        public static string? FromBrowsers(string? mediaTitle)
        {
            try
            {
                var pids = new HashSet<int>(Process.GetProcesses()
                    .Where(p => Browsers.Any(b => p.ProcessName.StartsWith(b, StringComparison.OrdinalIgnoreCase)))
                    .Select(p => p.Id));
                if (pids.Count == 0) return null;

                var windows = AutomationElement.RootElement
                    .FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition)
                    .Cast<AutomationElement>()
                    .Where(w => SafePid(w) is int pid && pids.Contains(pid))
                    .ToList();

                string title = (mediaTitle ?? "").Trim();
                var ordered = windows
                    .OrderByDescending(w => title.Length > 0 && SafeName(w).Contains(title, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(w => SafeName(w).Contains("YouTube", StringComparison.OrdinalIgnoreCase));

                foreach (var window in ordered)
                {
                    if (FindInAddressBar(window) is string id) return id;
                }
            }
            catch
            {
                // Acessibilidade indisponível: segue para a área de transferência.
            }
            return null;
        }

        /// <summary>
        /// Percorre a interface do navegador (sem entrar no conteúdo das páginas, que é enorme)
        /// atrás de um campo de texto cujo valor seja um link do YouTube.
        /// </summary>
        private static string? FindInAddressBar(AutomationElement window)
        {
            var walker = TreeWalker.ControlViewWalker;
            var queue = new Queue<(AutomationElement El, int Depth)>();
            queue.Enqueue((window, 0));
            int visited = 0;
            while (queue.Count > 0 && visited < 600)
            {
                var (el, depth) = queue.Dequeue();
                visited++;
                ControlType? type;
                try { type = el.Current.ControlType; } catch { continue; }
                if (type == ControlType.Document) continue; // conteúdo da página

                if (type == ControlType.Edit &&
                    el.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) &&
                    ExtractId(((ValuePattern)pattern).Current.Value) is string id)
                    return id;

                if (depth >= 14) continue;
                try
                {
                    for (var child = walker.GetFirstChild(el); child != null; child = walker.GetNextSibling(child))
                        queue.Enqueue((child, depth + 1));
                }
                catch
                {
                    // Elemento sumiu durante a leitura.
                }
            }
            return null;
        }

        private static int? SafePid(AutomationElement e)
        {
            try { return e.Current.ProcessId; } catch { return null; }
        }

        private static string SafeName(AutomationElement e)
        {
            try { return e.Current.Name ?? ""; } catch { return ""; }
        }
    }
}
