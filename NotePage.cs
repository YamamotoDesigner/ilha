using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Ilha
{
    /// <summary>
    /// Uma página das Notas (como uma página do Notion): título, cor e o texto com negrito,
    /// itálico e sublinhado. O texto fica num formato simples (&lt;b&gt;, &lt;i&gt;, &lt;u&gt;), que não guarda
    /// cores nem fontes, para combinar com o tema claro e o escuro.
    /// </summary>
    public sealed class NotePage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        /// <summary>red, green, yellow, blue, purple ou orange.</summary>
        public string Color { get; set; } = "blue";
        public string Content { get; set; } = "";
        /// <summary>Começo do texto, sem formatação, para a lista de páginas.</summary>
        public string Preview { get; set; } = "";
        public DateTime Updated { get; set; } = DateTime.Now;

        /// <summary>As cores das páginas, na ordem em que aparecem para escolher.</summary>
        public static readonly IReadOnlyList<(string Key, string Name, Color Value)> Colors = new[]
        {
            ("red", "Vermelho", System.Windows.Media.Color.FromRgb(0xFF, 0x45, 0x3A)),
            ("green", "Verde", System.Windows.Media.Color.FromRgb(0x30, 0xD1, 0x58)),
            ("yellow", "Amarelo", System.Windows.Media.Color.FromRgb(0xFF, 0xD6, 0x0A)),
            ("blue", "Azul", System.Windows.Media.Color.FromRgb(0x0A, 0x84, 0xFF)),
            ("purple", "Roxo", System.Windows.Media.Color.FromRgb(0xBF, 0x5A, 0xF2)),
            ("orange", "Laranja", System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x0A)),
        };

        public static Color ColorOf(string? key)
        {
            foreach (var c in Colors)
                if (c.Key == key) return c.Value;
            return Colors[3].Value;
        }
    }
}
