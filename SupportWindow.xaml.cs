using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace Ilha
{
    public partial class SupportWindow : Window
    {
        private static SupportWindow? _open;

        /// <summary>Abre a janela de apoio (ou traz para frente se já estiver aberta).</summary>
        public static void ShowSingle()
        {
            if (!Support.IsConfigured) return;
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
            string code = Support.PixCode;
            QrImage.Source = Support.CreateQr(code);
            CodeBox.Text = code;
            string? name = Support.ReceiverName(code);
            ReceiverText.Text = name != null ? $"Para: {name}" : "";
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(Support.PixCode);
                CopyButton.Content = "Copiado! Cole no app do seu banco ✓";
            }
            catch
            {
                CodeBox.Focus();
                CodeBox.SelectAll();
                CopyButton.Content = "Não deu para copiar — selecione o código acima";
            }
            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            reset.Tick += (_, _) =>
            {
                reset.Stop();
                CopyButton.Content = "Copiar código Pix";
            };
            reset.Start();
        }

        private void Link_Navigate(object sender, RequestNavigateEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
            e.Handled = true;
        }
    }
}
