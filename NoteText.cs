using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace Ilha
{
    /// <summary>
    /// Converte o texto das páginas entre o editor (FlowDocument) e um formato simples:
    /// uma linha por parágrafo, com &lt;b&gt;, &lt;i&gt; e &lt;u&gt; para negrito, itálico e sublinhado.
    /// Cores e fontes coladas de outros apps não são guardadas.
    /// </summary>
    public static class NoteText
    {
        public static string Escape(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        public static string FromPlain(string text) => Escape(text.Replace("\r\n", "\n").Replace('\r', '\n'));

        /// <summary>Texto sem formatação.</summary>
        public static string ToPlain(string content)
        {
            var sb = new StringBuilder();
            Parse(content, (text, _, _, _) => sb.Append(text), () => sb.Append('\n'));
            return sb.ToString();
        }

        /// <summary>Começo do texto em uma linha, para a lista de páginas.</summary>
        public static string PreviewOf(string plain)
        {
            var parts = plain.Replace("\r", "").Split('\n')
                .Select(l => l.Trim()).Where(l => l.Length > 0).Take(3);
            string s = string.Join(" · ", parts);
            return s.Length > 90 ? s[..90] + "…" : s;
        }

        public static string Save(FlowDocument doc)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var block in doc.Blocks)
            {
                if (!first) sb.Append('\n');
                first = false;
                if (block is Paragraph p)
                {
                    foreach (var inline in p.Inlines) Write(inline, sb);
                }
                else
                {
                    // Listas e outros blocos (raros aqui): guarda só o texto.
                    string text = new TextRange(block.ContentStart, block.ContentEnd).Text.TrimEnd('\r', '\n');
                    sb.Append(Escape(text.Replace("\r\n", "\n")));
                }
            }
            return sb.ToString();
        }

        private static void Write(Inline inline, StringBuilder sb)
        {
            switch (inline)
            {
                case Run run:
                    if (run.Text.Length == 0) return;
                    bool b = run.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight();
                    bool i = run.FontStyle == FontStyles.Italic;
                    bool u = IsUnderlined(run);
                    if (b) sb.Append("<b>");
                    if (i) sb.Append("<i>");
                    if (u) sb.Append("<u>");
                    sb.Append(Escape(run.Text.Replace("\r\n", "\n")));
                    if (u) sb.Append("</u>");
                    if (i) sb.Append("</i>");
                    if (b) sb.Append("</b>");
                    break;
                case Span span:
                    foreach (var child in span.Inlines) Write(child, sb);
                    break;
                case LineBreak:
                    sb.Append('\n');
                    break;
            }
        }

        /// <summary>O sublinhado pode estar no próprio trecho ou num dos que o envolvem.</summary>
        private static bool IsUnderlined(Inline inline)
        {
            for (DependencyObject? x = inline; x is Inline or Paragraph; x = (x as TextElement)?.Parent)
            {
                var decorations = x is Inline i ? i.TextDecorations : ((Paragraph)x).TextDecorations;
                if (decorations != null && decorations.Any(d => d.Location == TextDecorationLocation.Underline))
                    return true;
            }
            return false;
        }

        public static void Load(FlowDocument doc, string content)
        {
            doc.Blocks.Clear();
            var paragraph = new Paragraph();
            Parse(content ?? "",
                (text, b, i, u) =>
                {
                    var run = new Run(text);
                    if (b) run.FontWeight = FontWeights.Bold;
                    if (i) run.FontStyle = FontStyles.Italic;
                    if (u) run.TextDecorations = TextDecorations.Underline;
                    paragraph.Inlines.Add(run);
                },
                () =>
                {
                    doc.Blocks.Add(paragraph);
                    paragraph = new Paragraph();
                });
            doc.Blocks.Add(paragraph);
        }

        /// <summary>Lê o formato simples, chamando <paramref name="text"/> para cada trecho e <paramref name="newLine"/> a cada linha.</summary>
        private static void Parse(string content, Action<string, bool, bool, bool> text, Action newLine)
        {
            bool b = false, i = false, u = false;
            var current = new StringBuilder();
            void Flush()
            {
                if (current.Length == 0) return;
                text(current.ToString(), b, i, u);
                current.Clear();
            }

            for (int k = 0; k < content.Length; k++)
            {
                char c = content[k];
                if (c == '\n')
                {
                    Flush();
                    newLine();
                }
                else if (c == '<')
                {
                    int end = content.IndexOf('>', k);
                    if (end < 0) { current.Append(c); continue; }
                    string tag = content.Substring(k + 1, end - k - 1);
                    Flush();
                    switch (tag)
                    {
                        case "b": b = true; break;
                        case "/b": b = false; break;
                        case "i": i = true; break;
                        case "/i": i = false; break;
                        case "u": u = true; break;
                        case "/u": u = false; break;
                    }
                    k = end;
                }
                else if (c == '&')
                {
                    if (string.CompareOrdinal(content, k, "&amp;", 0, 5) == 0) { current.Append('&'); k += 4; }
                    else if (string.CompareOrdinal(content, k, "&lt;", 0, 4) == 0) { current.Append('<'); k += 3; }
                    else if (string.CompareOrdinal(content, k, "&gt;", 0, 4) == 0) { current.Append('>'); k += 3; }
                    else current.Append(c);
                }
                else
                {
                    current.Append(c);
                }
            }
            Flush();
        }
    }
}
