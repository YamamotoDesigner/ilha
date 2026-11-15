using System.Windows;
using System.Windows.Media;

namespace Ilha
{
    /// <summary>
    /// Modo claro ou escuro. Todas as cores das janelas vêm daqui (recursos dinâmicos do app):
    /// "Ink_XX" é a cor do texto/ícones com transparência XX (branco no escuro, quase preto no claro).
    /// </summary>
    internal static class ThemePalette
    {
        public static bool IsDark { get; private set; } = true;

        public static void Apply(bool dark)
        {
            IsDark = dark;
            var r = Application.Current.Resources;
            var ink = dark ? Colors.White : Color.FromRgb(0x14, 0x14, 0x18);
            for (int a = 0; a <= 0xFF; a++)
                r[$"Ink_{a:X2}"] = Frozen(Color.FromArgb((byte)a, ink.R, ink.G, ink.B));

            r["InkInverse"] = Frozen(dark ? Color.FromRgb(0x11, 0x11, 0x11) : Colors.White);
            r["Secondary"] = Frozen(Color.FromArgb(dark ? (byte)0xB8 : (byte)0xA8, ink.R, ink.G, ink.B));
            r["Tertiary"] = Frozen(Color.FromArgb(dark ? (byte)0x80 : (byte)0x80, ink.R, ink.G, ink.B));
            r["Hairline"] = Frozen(Color.FromArgb(dark ? (byte)0x1A : (byte)0x17, ink.R, ink.G, ink.B));

            // Janelas (configurações, apoio, apresentação)
            r["WinBg"] = Frozen(dark ? Hex(0x121214) : Hex(0xF4F4F6));
            r["WinFg"] = Frozen(dark ? Hex(0xF5F5F7) : Hex(0x1C1C1E));
            r["SideBg"] = Frozen(dark ? Hex(0x18181B) : Hex(0xEBEBEF));
            r["CardBg"] = Frozen(dark ? Hex(0x1C1C20) : Hex(0xFFFFFF));
            r["Line"] = Frozen(dark ? Hex(0x2A2A30) : Hex(0xE1E1E6));
            r["Text2"] = Frozen(dark ? Hex(0xA1A1A8) : Hex(0x5E5E66));
            r["Text3"] = Frozen(dark ? Hex(0x6E6E76) : Hex(0x8E8E96));
            r["Control"] = Frozen(dark ? Hex(0x2A2A30) : Hex(0xE6E6EB));
            r["ControlOff"] = Frozen(dark ? Hex(0x3A3A40) : Hex(0xD3D3D9));
        }

        public static void ApplyTo(Window window) =>
            window.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;

        private static Color Hex(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
