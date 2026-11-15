using System;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;

namespace Ilha
{
    /// <summary>Formato e transparência da janela da ilha.</summary>
    internal static class Glass
    {
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }

        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        /// <summary>
        /// Recorta a janela no formato da ilha: cantos de cima retos (colados na borda da tela)
        /// e cantos de baixo arredondados. Coordenadas em pixels físicos, relativas à janela.
        /// </summary>
        public static void SetIslandRegion(IntPtr hwnd, int left, int top, int right, int bottom, int radius)
        {
            int d = Math.Max(2, radius * 2);
            // Começa acima do topo da janela para que só os cantos de baixo fiquem arredondados.
            IntPtr region = CreateRoundRectRgn(left, top - d, right + 1, bottom + 1, d, d);
            if (SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region); // em caso de sucesso o Windows é o dono
        }

        /// <summary>
        /// Deixa a janela (não "layered") com fundo realmente transparente: o compositor do Windows
        /// passa a respeitar a transparência de cada pixel, e o vidro (Backdrop) aparece por trás.
        /// </summary>
        public static void MakeTransparent(IntPtr hwnd)
        {
            if (System.Windows.Interop.HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
                target.BackgroundColor = System.Windows.Media.Colors.Transparent;
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            try { DwmExtendFrameIntoClientArea(hwnd, ref margins); } catch (DllNotFoundException) { }
        }
    }

    /// <summary>Mola amortecida (como as animações do iOS/macOS): passa um pouco do alvo e assenta.</summary>
    internal sealed class SpringEase : EasingFunctionBase
    {
        public double Damping { get; set; } = 0.74;
        public double Frequency { get; set; } = 12.5;

        public SpringEase() => EasingMode = EasingMode.EaseIn; // usa a curva exatamente como calculada

        protected override double EaseInCore(double t)
        {
            double z = Damping, w = Frequency, wd = w * Math.Sqrt(1 - z * z);
            return 1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + z * w / wd * Math.Sin(wd * t));
        }

        protected override System.Windows.Freezable CreateInstanceCore() =>
            new SpringEase { Damping = Damping, Frequency = Frequency };
    }
}
