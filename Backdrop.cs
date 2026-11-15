using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace Ilha
{
    /// <summary>
    /// Vidro de verdade: um visual do compositor do Windows atrás do conteúdo WPF, pintado com o
    /// desfoque do que está atrás da janela (o mesmo pincel do acrílico do sistema) e recortado
    /// no formato exato da ilha, com cantos suavizados.
    /// </summary>
    internal sealed class Backdrop
    {
        private readonly Compositor _compositor;
        private readonly DesktopWindowTarget _target;
        private readonly SpriteVisual _visual;
        private readonly CompositionRoundedRectangleGeometry _shape;

        private static object? _dispatcherQueueController;

        private Backdrop(IntPtr hwnd)
        {
            EnsureDispatcherQueue();
            _compositor = new Compositor();
            var interop = _compositor.As<ICompositorDesktopInterop>();
            interop.CreateDesktopWindowTarget(hwnd, false, out IntPtr raw);
            _target = DesktopWindowTarget.FromAbi(raw);
            Marshal.Release(raw);

            _shape = _compositor.CreateRoundedRectangleGeometry();
            _visual = _compositor.CreateSpriteVisual();
            _visual.Brush = _compositor.CreateHostBackdropBrush();
            _visual.Clip = _compositor.CreateGeometricClip(_shape);
            _target.Root = _visual;
        }

        /// <summary>Cria o vidro, ou null se o Windows não tiver suporte (Windows 10 antigo, etc.).</summary>
        public static Backdrop? TryCreate(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(hwnd, 17 /* DWMWA_USE_HOSTBACKDROPBRUSH */, ref on, sizeof(int));
                return new Backdrop(hwnd);
            }
            catch
            {
                return null;
            }
        }

        public bool Visible
        {
            get => _visual.IsVisible;
            set => _visual.IsVisible = value;
        }

        /// <summary>
        /// Posiciona o vidro (pixels físicos, relativos à janela). Os cantos de cima ficam retos:
        /// a forma começa acima do topo da janela, então só os de baixo aparecem arredondados.
        /// </summary>
        public void SetBounds(float left, float width, float height, float radius)
        {
            _visual.Offset = new Vector3(left, 0, 0);
            _visual.Size = new Vector2(width, height);
            _shape.Offset = new Vector2(0, -radius);
            _shape.Size = new Vector2(width, height + radius);
            _shape.CornerRadius = new Vector2(radius, radius);
        }

        private static void EnsureDispatcherQueue()
        {
            if (DispatcherQueue.GetForCurrentThread() != null) return;
            var options = new DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                threadType = 2,    // DQTYPE_THREAD_CURRENT
                apartmentType = 2, // DQTAT_COM_STA
            };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out IntPtr controller));
            _dispatcherQueueController = MarshalInspectable<object>.FromAbi(controller); // mantém viva
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DispatcherQueueOptions
        {
            public int dwSize;
            public int threadType;
            public int apartmentType;
        }

        [DllImport("CoreMessaging.dll")]
        private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICompositorDesktopInterop
        {
            void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out IntPtr target);
            void EnsureOnThread(uint threadId);
        }
    }
}
