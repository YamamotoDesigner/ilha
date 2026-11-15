using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace Ilha
{
    /// <summary>
    /// Picture in picture: janela pequena, sempre por cima, que toca um vídeo do YouTube
    /// (player incorporado, num WebView2) a partir do ponto em que ele estava no navegador.
    /// </summary>
    public partial class PipWindow : Window
    {
        private const string Host = "ilha.pip";
        private static PipWindow? _open;
        private static readonly string DataFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ilha");

        /// <summary>Testes de desenvolvimento: toca sem som.</summary>
        internal static bool TestMute;

        private string? _videoId;
        private int _start;
        private Task? _init;

        /// <summary>Abre (ou reaproveita) a janela. Sem <paramref name="videoId"/>, pede um link.</summary>
        public static void Open(string? videoId, double startSeconds, string? title = null)
        {
            if (_open == null)
            {
                _open = new PipWindow();
                _open.Closed += (_, _) => _open = null;
                _open.PlaceBottomRight();
                _open.Show();
            }
            _open.Activate();
            if (videoId != null) _ = _open.PlayAsync(videoId, (int)Math.Max(0, startSeconds), title);
            else _open.ShowPaste(null);
        }

        public static void CloseIfOpen() => _open?.Close();

        private PipWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) =>
            {
                // Cantos arredondados do Windows 11 numa janela sem moldura.
                int round = 2; // DWMWCP_ROUND
                try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref round, sizeof(int)); } catch { }
            };
        }

        private void PlaceBottomRight()
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 24;
            Top = area.Bottom - Height - 24;
        }

        private Task EnsureWebViewAsync() => _init ??= InitWebViewAsync();

        private async Task InitWebViewAsync()
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(DataFolder, "WebView2"),
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            await Web.EnsureCoreWebView2Async(env);

            // Página local com o player: o YouTube exige que o vídeo incorporado venha de um site com endereço.
            string folder = Path.Combine(DataFolder, "pip");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "player.html"), PlayerHtml);
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(Host, folder, CoreWebView2HostResourceAccessKind.Allow);
            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            // Links clicados dentro do player abrem no navegador, não aqui.
            Web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                OpenUrl(e.Uri);
            };
        }

        private async Task PlayAsync(string videoId, int start, string? title = null)
        {
            _videoId = videoId;
            _start = start;
            PastePanel.Visibility = Visibility.Collapsed;
            Web.Visibility = Visibility.Visible;
            TitleText.Text = string.IsNullOrWhiteSpace(title) ? "Picture in picture" : title;
            try
            {
                await EnsureWebViewAsync();
                Web.CoreWebView2.Navigate($"https://{Host}/player.html?v={videoId}&t={start}{(TestMute ? "&m=1" : "")}");
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ShowPaste("Falta o componente WebView2 do Windows. Baixe em go.microsoft.com/fwlink/p/?LinkId=2124703 e tente de novo.");
            }
            catch (Exception ex)
            {
                ShowPaste("Não foi possível abrir o player: " + ex.Message);
            }
        }

        private void ShowPaste(string? message)
        {
            Web.Visibility = Visibility.Collapsed;
            PastePanel.Visibility = Visibility.Visible;
            PasteHint.Text = message ?? "Não encontrei um vídeo do YouTube aberto no navegador. Cole o link aqui:";
            if (YouTubeLocator.FromClipboard() is string id && message == null)
                LinkBox.Text = $"https://youtu.be/{id}";
            LinkBox.Focus();
            LinkBox.SelectAll();
        }

        private void PasteGo_Click(object sender, RoutedEventArgs e) => PlayFromBox();

        private void LinkBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) PlayFromBox();
        }

        private void PlayFromBox()
        {
            if (YouTubeLocator.ExtractId(LinkBox.Text) is string id) _ = PlayAsync(id, 0);
            else PasteHint.Text = "Esse link não parece ser de um vídeo do YouTube. Tente copiar de novo.";
        }

        private void NewLink_Click(object sender, RoutedEventArgs e)
        {
            LinkBox.Text = "";
            ShowPaste("Cole o link de outro vídeo do YouTube:");
        }

        private void OpenYouTube_Click(object sender, RoutedEventArgs e)
        {
            if (_videoId != null) OpenUrl($"https://www.youtube.com/watch?v={_videoId}&t={_start}s");
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Web.Dispose();
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const string PlayerHtml = """
            <!doctype html>
            <html><head><meta charset="utf-8"><title>player</title>
            <meta name="referrer" content="strict-origin-when-cross-origin">
            <style>html,body{margin:0;height:100%;background:#000;overflow:hidden}iframe{border:0;width:100%;height:100%}</style>
            </head><body>
            <script>
              const q = new URLSearchParams(location.search);
              const id = (q.get('v') || '').replace(/[^A-Za-z0-9_-]/g, '');
              const t = parseInt(q.get('t') || '0', 10) || 0;
              const f = document.createElement('iframe');
              f.src = 'https://www.youtube.com/embed/' + id + '?autoplay=1&start=' + t +
                      '&rel=0&playsinline=1&modestbranding=1&hl=pt-BR&cc_lang_pref=pt-BR' + (q.get('m') === '1' ? '&mute=1' : '') +
                      '&origin=' + encodeURIComponent(location.origin);
              f.allow = 'autoplay; encrypted-media; picture-in-picture; fullscreen';
              f.allowFullscreen = true;
              f.referrerPolicy = 'strict-origin-when-cross-origin';
              document.body.appendChild(f);
            </script>
            </body></html>
            """;
    }
}
