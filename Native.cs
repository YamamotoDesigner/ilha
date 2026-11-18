using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ilha
{
    internal static class Native
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_TOPMOST = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

        public const int WM_HOTKEY = 0x0312;
        public const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hWnd);


        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        public static IntPtr ForegroundWindow() => GetForegroundWindow();

        /// <summary>Some do Alt+Tab e, por padrão, nunca rouba o foco de outros apps.</summary>
        public static void MakeToolWindow(IntPtr hwnd)
        {
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        /// <summary>
        /// Liga/desliga WS_EX_NOACTIVATE. Desligado só enquanto o cursor está sobre as notas,
        /// para que o próprio clique do usuário ative a janela (o Windows bloqueia ativação programática).
        /// </summary>
        public static void SetNoActivate(IntPtr hwnd, bool noActivate)
        {
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            int updated = noActivate ? ex | WS_EX_NOACTIVATE : ex & ~WS_EX_NOACTIVATE;
            if (updated != ex) SetWindowLong(hwnd, GWL_EXSTYLE, updated);
        }

        public static void MoveTopmost(IntPtr hwnd, int x, int y) =>
            SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);

        public static void MakeTopmost(IntPtr hwnd) =>
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);

        /// <summary>True quando a janela em primeiro plano cobre o monitor inteiro (jogo, vídeo em tela cheia).</summary>
        public static bool IsForegroundFullscreen(System.Drawing.Rectangle monitor, IntPtr self)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == self || fg == GetShellWindow() || fg == GetDesktopWindow())
                return false;

            var cls = new StringBuilder(64);
            GetClassName(fg, cls, cls.Capacity);
            string name = cls.ToString();
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return false;

            if (!GetWindowRect(fg, out RECT r))
                return false;

            return r.Left <= monitor.Left && r.Top <= monitor.Top &&
                   r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
        }

        // ───────────── App em primeiro plano (ilha discreta) ─────────────

        public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        /// <summary>Avisa (na própria thread da interface) sempre que outra janela vem para a frente.</summary>
        public static IntPtr WatchForeground(WinEventProc callback) =>
            SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, callback, 0, 0, 0x0000 | 0x0002); // EVENT_SYSTEM_FOREGROUND, fora do processo, ignora o próprio

        public static void StopWatching(IntPtr hook)
        {
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
        }

        /// <summary>
        /// true: a área de trabalho está na frente; false: há um app na frente;
        /// null: algo passageiro (barra de tarefas, menu Iniciar, a própria ilha) — mantém como estava.
        /// </summary>
        public static bool? IsDesktopInFront()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return null;
            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == (uint)Environment.ProcessId) return null;
            if (fg == GetShellWindow()) return true;

            var cls = new StringBuilder(96);
            GetClassName(fg, cls, cls.Capacity);
            return cls.ToString() switch
            {
                "Progman" or "WorkerW" => true,
                "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or
                "TopLevelWindowForOverflowXamlIsland" or "Windows.UI.Core.CoreWindow" or
                "XamlExplorerHostIslandWindow" or "MultitaskingViewFrame" or "ForegroundStaging" or
                "TaskListThumbnailWnd" or "Shell_InputSwitchTopLevelWindow" or "#32768" => null,
                _ => false,
            };
        }

        // ───────────── Janela embaixo da ilha (ilha dinâmica) ─────────────

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetCloaked(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);

        /// <summary>Títulos das janelas principais abertas (inclusive minimizadas).</summary>
        public static List<string> WindowTitles()
        {
            var list = new List<string>();
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                int len = GetWindowTextLength(hwnd);
                if (len <= 0 || len > 1000) return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                list.Add(sb.ToString());
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>
        /// Olha as janelas de cima para baixo e diz se a primeira que encosta na faixa da ilha é
        /// um app de verdade (navegador, editor… com os botões do topo ali embaixo). Janelas
        /// minimizadas, escondidas, em outra área de trabalho virtual ou transparentes não contam.
        /// </summary>
        public static bool IsAppUnder(RECT strip)
        {
            bool found = false;
            uint self = (uint)Environment.ProcessId;
            var cls = new StringBuilder(96);
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == self) return true;
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                if ((ex & (WS_EX_TOOLWINDOW | 0x20 /*TRANSPARENT*/)) != 0) return true;
                if (DwmGetCloaked(hwnd, 14 /*DWMWA_CLOAKED*/, out int cloaked, 4) == 0 && cloaked != 0) return true;

                if (DwmGetWindowAttribute(hwnd, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out RECT r, Marshal.SizeOf<RECT>()) != 0 &&
                    !GetWindowRect(hwnd, out r)) return true;
                if (r.Right - r.Left < 8 || r.Bottom - r.Top < 8) return true;
                if (r.Left >= strip.Right || r.Right <= strip.Left || r.Top >= strip.Bottom || r.Bottom <= strip.Top) return true;

                cls.Clear();
                GetClassName(hwnd, cls, cls.Capacity);
                switch (cls.ToString())
                {
                    case "Progman" or "WorkerW":
                        return false; // chegou na área de trabalho: nada cobrindo
                    case "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or
                         "TopLevelWindowForOverflowXamlIsland" or "Windows.UI.Core.CoreWindow" or
                         "XamlExplorerHostIslandWindow" or "MultitaskingViewFrame" or "ForegroundStaging" or
                         "TaskListThumbnailWnd" or "Shell_InputSwitchTopLevelWindow" or "#32768" or
                         "tooltips_class32" or "SysShadow":
                        return true; // passageiro: olha o que está atrás
                }
                found = true;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Avisa quando uma janela é minimizada/restaurada ou termina de ser movida.</summary>
        public static IntPtr WatchWindowChanges(WinEventProc callback) =>
            SetWinEventHook(0x000B, 0x0017, IntPtr.Zero, callback, 0, 0, 0x0000 | 0x0002); // MOVESIZEEND..MINIMIZEEND

        public static bool LeftButtonDown => (GetAsyncKeyState(0x01) & 0x8000) != 0;

        public static POINT CursorPosition()
        {
            GetCursorPos(out POINT p);
            return p;
        }

        public static bool WindowRect(IntPtr hwnd, out RECT rect) => GetWindowRect(hwnd, out rect);

        /// <summary>Ícone do shell para um arquivo ou pasta.</summary>
        public static ImageSource? GetShellIcon(string path)
        {
            const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;
            var info = new SHFILEINFO();
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON) == IntPtr.Zero
                || info.hIcon == IntPtr.Zero)
                return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
    }
}
