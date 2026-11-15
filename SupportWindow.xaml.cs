using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Ilha
{
    /// <summary>Janela "Apoie o Ilha": doação via Pix e outras formas de ajudar o projeto.</summary>
    public partial class SupportWindow : Window
    {
        private const string SiteUrl = "https://yamamotodesigner.github.io/ilha/";
        private static SupportWindow? _open;

        /// <summary>Abre a janela (ou traz para frente se já estiver aberta).</summary>
        public static void ShowSingle()
        {
            if (_open != null)
            {
                _open.Activate();
                return;
            }
            _open = new SupportWindow();
            _open.Closed += (_, _) => _open = null;
            _open.Show();
            _open.Activate();
        }

        private SupportWindow()
        {
            InitializeComponent();
            ThemePalette.ApplyTo(this);
            if (Support.IsConfigured)
            {
                string code = Support.PixCode;
                QrImage.Source = Support.CreateQr(code);
                CodeBox.Text = code;
                string? name = Support.ReceiverName(code);
                ReceiverText.Text = name != null ? $"Para: {name}" : "";
            }
            else
            {
                // Sem Pix configurado: só as outras formas de ajudar.
                PixCard.Visibility = Visibility.Collapsed;
                PixCol.Width = new GridLength(0);
                PixGap.Width = new GridLength(0);
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (TryCopy(Support.PixCode))
            {
                CopyText.Text = L.T("Copiado! Cole no app do seu banco ✓");
            }
            else
            {
                CodeBox.Visibility = Visibility.Visible;
                CodeBox.Focus();
                CodeBox.SelectAll();
                CopyText.Text = L.T("Não deu para copiar — selecione o código abaixo");
            }
            After(3, () => CopyText.Text = L.T("Copiar código Pix (copia e cola)"));
        }

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            ShareText.Text = TryCopy(SiteUrl) ? L.T("Link copiado! Agora é só colar na conversa ✓") : SiteUrl;
            After(3, () => ShareText.Text = L.T("Copia o link do site para você mandar."));
        }

        private void Link_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string url }) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Sem navegador padrão.
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static bool TryCopy(string text)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void After(double seconds, Action action)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
        }
    }
}
