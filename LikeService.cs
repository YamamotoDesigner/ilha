using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ilha
{
    /// <summary>
    /// "Curtir" a música do Spotify. O Spotify não oferece um comando para outros apps, então o
    /// Ilha usa o atalho do próprio app (Alt + Shift + B, "Curtir/Salvar em Músicas Curtidas"):
    /// traz a janela do Spotify por um instante, envia o atalho e devolve o foco a quem estava antes.
    /// O atalho alterna: usar de novo tira a curtida.
    /// </summary>
    internal static class LikeService
    {
        public static bool IsSpotify(string sourceAppId) =>
            sourceAppId.Contains("Spotify", StringComparison.OrdinalIgnoreCase);

        /// <summary>Envia o atalho de curtir. Devolve false se o Spotify não foi encontrado.</summary>
        public static bool ToggleSpotifyLike()
        {
            var spotify = Process.GetProcessesByName("Spotify")
                .Select(p => p.MainWindowHandle)
                .FirstOrDefault(h => h != IntPtr.Zero);
            if (spotify == IntPtr.Zero) return false;

            IntPtr previous = GetForegroundWindow();
            bool wasMinimized = IsIconic(spotify);

            // Um toque em Alt libera a troca de janela em primeiro plano para este processo.
            Key(VK_MENU, down: true);
            if (wasMinimized) ShowWindow(spotify, SW_SHOWNOACTIVATE);
            SetForegroundWindow(spotify);
            Thread.Sleep(60);
            Key(VK_SHIFT, down: true);
            Key(VK_B, down: true);
            Key(VK_B, down: false);
            Key(VK_SHIFT, down: false);
            Key(VK_MENU, down: false);
            Thread.Sleep(80);

            if (wasMinimized) ShowWindow(spotify, SW_MINIMIZE);
            if (previous != IntPtr.Zero && previous != spotify) SetForegroundWindow(previous);
            return true;
        }

        /// <summary>Aleatório no Spotify (atalho Ctrl + S do próprio app).</summary>
        public static bool ToggleSpotifyShuffle() => SendCtrlShortcut(0x53);

        /// <summary>Repetir no Spotify (atalho Ctrl + R: desligado → playlist → música → desligado).</summary>
        public static bool CycleSpotifyRepeat() => SendCtrlShortcut(0x52);

        /// <summary>
        /// Ctrl + tecla na janela do Spotify. O Alt pressionado libera a troca de janela; ele é solto
        /// com o Ctrl já apertado, para não abrir o menu do app.
        /// </summary>
        private static bool SendCtrlShortcut(ushort key)
        {
            var spotify = Process.GetProcessesByName("Spotify")
                .Select(p => p.MainWindowHandle)
                .FirstOrDefault(h => h != IntPtr.Zero);
            if (spotify == IntPtr.Zero) return false;

            IntPtr previous = GetForegroundWindow();
            bool wasMinimized = IsIconic(spotify);
            Key(VK_MENU, down: true);
            if (wasMinimized) ShowWindow(spotify, SW_SHOWNOACTIVATE);
            SetForegroundWindow(spotify);
            Thread.Sleep(60);
            Key(VK_CONTROL, down: true);
            Key(VK_MENU, down: false);
            Key(key, down: true);
            Key(key, down: false);
            Key(VK_CONTROL, down: false);
            Thread.Sleep(80);

            if (wasMinimized) ShowWindow(spotify, SW_MINIMIZE);
            if (previous != IntPtr.Zero && previous != spotify) SetForegroundWindow(previous);
            return true;
        }

        private const ushort VK_MENU = 0x12, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_B = 0x42;
        private const int SW_SHOWNOACTIVATE = 4, SW_MINIMIZE = 6;

        private static void Key(ushort vk, bool down)
        {
            var input = new Input
            {
                Type = 1, // INPUT_KEYBOARD
                Ki = new KeyboardInput { Vk = vk, Flags = down ? 0u : 2u /* KEYEVENTF_KEYUP */ },
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort Vk, Scan;
            public uint Flags, Time;
            public IntPtr Extra;
        }

        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct Input
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public KeyboardInput Ki;
        }

        [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    }
}
