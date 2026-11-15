using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace Ilha
{
    /// <summary>Ícone na bandeja do sistema.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly Forms.NotifyIcon _icon;
        private string? _updateLabel;
        private Action? _installUpdate;

        public TrayIcon(Action openSettings, Action toggleIsland, Action exit)
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Opening += (_, e) =>
            {
                menu.Items.Clear();
                if (_updateLabel != null && _installUpdate != null)
                {
                    var install = _installUpdate;
                    var item = new Forms.ToolStripMenuItem(_updateLabel) { Font = new Font(menu.Font, FontStyle.Bold) };
                    item.Click += (_, _) => install();
                    menu.Items.Add(item);
                    menu.Items.Add(new Forms.ToolStripSeparator());
                }
                menu.Items.Add("Abrir/fechar ilha  (Ctrl+Alt+N)", null, (_, _) => toggleIsland());
                menu.Items.Add("Configurações…", null, (_, _) => openSettings());

                var startup = new Forms.ToolStripMenuItem("Iniciar com o Windows") { Checked = Startup.IsEnabled };
                startup.Click += (_, _) => Startup.Set(!Startup.IsEnabled);
                menu.Items.Add(startup);

                menu.Items.Add(new Forms.ToolStripSeparator());
                if (Support.IsConfigured)
                    menu.Items.Add("Apoiar o projeto ❤", null, (_, _) => SupportWindow.ShowSingle());
                menu.Items.Add("Sair", null, (_, _) => exit());
                e.Cancel = false;
            };

            _icon = new Forms.NotifyIcon
            {
                Text = "Ilha",
                Icon = CreateIcon(),
                ContextMenuStrip = menu,
                Visible = true,
            };
            _icon.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) openSettings();
            };
            _icon.BalloonTipClicked += (_, _) => _installUpdate?.Invoke();
        }

        /// <summary>Mostra (ou remove, com null) a opção de instalar atualização no menu.</summary>
        public void SetUpdate(string? label, Action? install, bool notify)
        {
            _updateLabel = label;
            _installUpdate = install;
            if (notify && label != null)
                _icon.ShowBalloonTip(8000, "Atualização do Ilha disponível", label + " — clique para instalar.", Forms.ToolTipIcon.Info);
        }

        private static Icon CreateIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var path = new GraphicsPath();
                var r = new Rectangle(3, 10, 26, 12);
                int d = 12;
                path.AddArc(r.Left, r.Top, d, d, 90, 180);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 180);
                path.CloseFigure();
                g.FillPath(Brushes.White, path);
                using var dot = new SolidBrush(Color.FromArgb(255, 60, 200, 120));
                g.FillEllipse(dot, 21, 13, 6, 6);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
