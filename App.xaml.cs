using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace Ilha
{
    public partial class App : Application
    {
        private Mutex? _singleInstance;
        private bool _ownsMutex;

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
            // Ao reiniciar (troca de idioma), espera a instância anterior terminar de fechar.
            bool restarting = e.Args.Contains("--restart");
            _singleInstance = new Mutex(false, mutexName);
            try
            {
                _ownsMutex = _singleInstance.WaitOne(restarting ? 10000 : 0);
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }
            if (!_ownsMutex)
            {
                Shutdown();
                return;
            }

            // O app só fecha pelo "Sair" (a apresentação fecha antes da ilha abrir).
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Resources["UiFont"] = UiFont;
            base.OnStartup(e);

            var settings = AppSettings.Load();
#if DEBUG
            // Testes visuais: idioma e tema só nesta execução (nada é salvo).
            if (Environment.GetEnvironmentVariable("ILHA_TEST_LANG") is { Length: > 0 } testLang) settings.Language = testLang;
            if (Environment.GetEnvironmentVariable("ILHA_TEST_THEME") is { Length: > 0 } testTheme) settings.Theme = testTheme;
#endif
            L.Set(settings.Language ?? L.SystemDefault);
            ThemePalette.Apply(!string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase));

            bool showOnboarding = !settings.OnboardingDone;
#if DEBUG
            if (Environment.GetEnvironmentVariable("ILHA_TEST_TAB") is { Length: > 0 }) showOnboarding = false;
            if (Environment.GetEnvironmentVariable("ILHA_TEST_ONBOARD") == "1") showOnboarding = true;
#endif
            if (showOnboarding)
            {
                var welcome = new OnboardingWindow(settings, replay: false);
                welcome.Finished += StartIsland;
                welcome.Show();
            }
            else
            {
                StartIsland();
            }
        }

        private static void StartIsland()
        {
            // O vidro fica numa janela própria, logo atrás da ilha (ver BackdropWindow).
            var glass = new BackdropWindow();
            glass.Show();
            new MainWindow(glass).Show();
        }

        /// <summary>Abre uma nova instância (que espera esta fechar) e encerra esta.</summary>
        public static void Restart(Action exit)
        {
            try
            {
                if (Environment.ProcessPath is string exe)
                    Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = false });
            }
            catch
            {
                // Sem conseguir reabrir: só fecha; o usuário abre de novo.
            }
            exit();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_ownsMutex)
            {
                try { _singleInstance?.ReleaseMutex(); } catch { }
            }
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
