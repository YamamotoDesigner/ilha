using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace Ilha
{
    public partial class App : Application
    {
        private Mutex? _singleInstance;

        /// <summary>
        /// SF Pro quando estiver instalada (a licença da Apple não permite distribuí-la junto);
        /// senão a Inter, que vem embutida no app e tem desenho bem parecido.
        /// </summary>
        public static readonly FontFamily UiFont = new(new Uri("pack://application:,,,/"),
            "SF Pro Display, SF Pro Text, ./Fonts/#Inter, Segoe UI Variable Text, Segoe UI");

        protected override void OnStartup(StartupEventArgs e)
        {
            string mutexName = "Ilha.SingleInstance";
#if DEBUG
            // Build de desenvolvimento pode rodar ao lado do Ilha instalado.
            if (Environment.GetEnvironmentVariable("ILHA_TEST") == "1") mutexName += ".Test";
#endif
            _singleInstance = new Mutex(true, mutexName, out bool isFirst);
            if (!isFirst)
            {
                Shutdown();
                return;
            }

            Resources["UiFont"] = UiFont;
            base.OnStartup(e);
            // O vidro fica numa janela própria, logo atrás da ilha (ver BackdropWindow).
            var glass = new BackdropWindow();
            glass.Show();
            new MainWindow(glass).Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
