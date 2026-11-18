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
    /// dá o foco à janela do Spotify (transparente, sem aparecer), envia o atalho e devolve o foco.
    /// O atalho alterna: usar de novo tira a curtida.
    /// </summary>
    internal static class LikeService
    {
        public static bool IsSpotify(string sourceAppId) =>
            sourceAppId.Contains("Spotify", StringComparison.OrdinalIgnoreCase);

        /// <summary>Envia o atalho de curtir. Devolve false se o Spotify não foi encontrado.</summary>
        public static bool ToggleSpotifyLike() => WithSpotifyFocused(() =>
        {
            Key(VK_SHIFT, down: true);
            Key(VK_B, down: true);
            Key(VK_B, down: false);
            Key(VK_SHIFT, down: false);
            Key(VK_MENU, down: false);
        });

        /// <summary>Aleatório no Spotify (atalho Ctrl + S do próprio app).</summary>
        public static bool ToggleSpotifyShuffle() => SendCtrlShortcut(0x53);

        /// <summary>Repetir no Spotify (atalho Ctrl + R: desligado → playlist → música → desligado).</summary>
        public static bool CycleSpotifyRepeat() => SendCtrlShortcut(0x52);

        /// <summary>
        /// Ctrl + tecla na janela do Spotify. O Alt pressionado libera a troca de janela; ele é solto
        /// com o Ctrl já apertado, para não abrir o menu do app.
        /// </summary>
        private static bool SendCtrlShortcut(ushort key) => WithSpotifyFocused(() =>
        {
            Key(VK_CONTROL, down: true);
            Key(VK_MENU, down: false);
            Key(key, down: true);
            Key(key, down: false);
            Key(VK_CONTROL, down: false);
        });

        /// <summary>
        /// O atalho só chega no Spotify com a janela dele em primeiro plano. Para a pessoa não ver
        /// nada piscar, a janela fica 100% transparente durante o toque: recebe o foco, o atalho é
        /// enviado, o foco volta para quem estava antes e a janela retorna ao mesmo lugar (minimizada
        /// ou atrás das outras), só então voltando a ficar visível. O Alt já vem apertado ao entrar
        /// em <paramref name="keys"/> (é ele que libera a troca de janela) e deve ser solto lá dentro.
        /// </summary>
        private static bool WithSpotifyFocused(Action keys)
        {
            var spotify = Process.GetProcessesByName("Spotify")
                .Select(p => p.MainWindowHandle)
                .FirstOrDefault(h => h != IntPtr.Zero);
            if (spotify == IntPtr.Zero) return false;

            IntPtr previous = GetForegroundWindow();
            if (previous == spotify)
            {
                // Já está na frente: só o atalho.
                Key(VK_MENU, down: true);
                keys();
                return true;
            }

            bool wasMinimized = IsIconic(spotify);
            IntPtr above = GetWindow(spotify, GW_HWNDPREV);
            int ex = GetWindowLong(spotify, GWL_EXSTYLE);
            bool hadLayered = (ex & WS_EX_LAYERED) != 0;
            uint oldKey = 0, oldFlags = 0;
            byte oldAlpha = 255;
            bool hidden = false;
            try
            {
                if (hadLayered) GetLayeredWindowAttributes(spotify, out oldKey, out oldAlpha, out oldFlags);
                else SetWindowLong(spotify, GWL_EXSTYLE, ex | WS_EX_LAYERED);
                hidden = SetLayeredWindowAttributes(spotify, 0, 0, LWA_ALPHA);

                // Um toque em Alt libera a troca de janela em primeiro plano para este processo.
                Key(VK_MENU, down: true);
                if (wasMinimized) ShowWindow(spotify, SW_SHOWNOACTIVATE);
                SetForegroundWindow(spotify);
                Thread.Sleep(60);
                keys();
                Thread.Sleep(80);

                if (previous != IntPtr.Zero) SetForegroundWindow(previous);
                if (wasMinimized)
                {
                    ShowWindow(spotify, SW_MINIMIZE);
                    Thread.Sleep(hidden ? 320 : 0); // a animação de minimizar acontece invisível
                }
                else
                {
                    // Volta para a mesma posição na pilha de janelas (atrás de quem estava na frente).
                    SetWindowPos(spotify, above != IntPtr.Zero ? above : HWND_BOTTOM, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    Thread.Sleep(30);
                }
            }
            finally
            {
                if (hadLayered) SetLayeredWindowAttributes(spotify, oldKey, oldAlpha, oldFlags);
                else
                {
                    SetLayeredWindowAttributes(spotify, 0, 255, LWA_ALPHA);
                    SetWindowLong(spotify, GWL_EXSTYLE, ex);
                }
            }
            return true;
        }

        private const ushort VK_MENU = 0x12, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_B = 0x42;
        private const int SW_SHOWNOACTIVATE = 4, SW_MINIMIZE = 6;
        private const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000;
        private const uint LWA_ALPHA = 2, GW_HWNDPREV = 3;
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        private static readonly IntPtr HWND_BOTTOM = new(1);

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
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
