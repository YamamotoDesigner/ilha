using System.Threading;
using System.Windows;

namespace Ilha
{
    public partial class App : Application
    {
        private Mutex? _singleInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstance = new Mutex(true, "Ilha.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);
            new MainWindow().Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
