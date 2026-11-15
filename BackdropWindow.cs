using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Ilha
{
    /// <summary>
    /// Janela sem conteúdo que fica logo atrás da ilha e carrega o vidro (Backdrop). Fica separada
    /// porque o compositor desenha o vidro por cima do conteúdo da própria janela; assim a ilha
    /// (dona do conteúdo) continua sempre na frente.
    /// </summary>
    public sealed class BackdropWindow : Window
    {
        private Backdrop? _backdrop;
        private IntPtr _hwnd;

        public BackdropWindow()
        {
            Title = "Ilha (vidro)";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = Brushes.Transparent;
            Width = Height = 1;
            Left = Top = -10000;
            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(_hwnd);
                Glass.MakeTransparent(_hwnd);
                Glass.SetIslandRegion(_hwnd, 0, 0, 0, 0, 0); // nada clicável até a ilha se posicionar
                _backdrop = Backdrop.TryCreate(_hwnd);
            };
        }

        /// <summary>O Windows suporta o vidro (desfoque do que está atrás).</summary>
        public bool Supported => _backdrop != null;

        public bool GlassVisible
        {
            set
            {
                if (_backdrop != null) _backdrop.Visible = value;
            }
        }

        /// <summary>Mesma posição e tamanho da janela da ilha, em pixels físicos.</summary>
        public void Follow(IntPtr island)
        {
            if (_hwnd == IntPtr.Zero || !GetWindowRect(island, out var r)) return;
            SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, 0x0010 /* NOACTIVATE */ | 0x0004 /* NOZORDER */);
        }

        /// <summary>Formato da ilha (pixels físicos relativos à janela).</summary>
        public void SetShape(float left, float width, float height, float radius, bool roundTop)
        {
            if (_hwnd == IntPtr.Zero) return;
            _backdrop?.SetBounds(left, width, height, radius, roundTop);
            // A região só serve para os cliques: fora da ilha, passam para o que está embaixo.
            Glass.SetIslandRegion(_hwnd, (int)left, 0, (int)(left + width), (int)height, (int)radius, roundTop);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
