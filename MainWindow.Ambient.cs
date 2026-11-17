using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Ilha
{
    /// <summary>
    /// Luz ambiente e movimento: a capa da música vira um brilho colorido atrás da ilha, a cor da
    /// capa pinta o progresso e as barrinhas, e os blocos entram em cascata ao abrir ou trocar de aba.
    /// </summary>
    public partial class MainWindow
    {
        private ImageSource? _ambientSource;
        private bool _ambientHasArt;

        /// <summary>Atualiza o brilho e a cor quando a capa muda.</summary>
        private void UpdateAmbient(bool force = false)
        {
            var art = _media.HasSession ? _media.Art as BitmapSource : null;
            if (!force && ReferenceEquals(art, _ambientSource)) return;
            _ambientSource = art;

            var accent = ((SolidColorBrush)Resources["Accent"]).Color;
            Color color = accent;
            BitmapSource? tiny = null;
            if (art != null)
            {
                try
                {
                    // 16×16 basta: esticada com suavização, vira um degradê macio, sem custo de desfoque.
                    tiny = new TransformedBitmap(art, new ScaleTransform(16.0 / art.PixelWidth, 16.0 / art.PixelHeight));
                    var bgra = new FormatConvertedBitmap(tiny, PixelFormats.Bgra32, null, 0);
                    bgra.Freeze();
                    tiny = bgra;
                    color = VividColor(bgra, accent);
                }
                catch
                {
                    tiny = null;
                }
            }

            _ambientHasArt = tiny != null;
            AmbientBrush.ImageSource = tiny;
            SetMediaAccent(color);
            UpdateAmbientVisibility(animate: true);
        }

        /// <summary>A cor mais "viva" da capa (média ponderada pela saturação), ajustada para ler bem.</summary>
        private static Color VividColor(BitmapSource bgra, Color fallback)
        {
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            double r = 0, g = 0, b = 0, total = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                double pb = px[i] / 255.0, pg = px[i + 1] / 255.0, pr = px[i + 2] / 255.0;
                double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
                double sat = max <= 0 ? 0 : (max - min) / max;
                double weight = sat * sat * (0.25 + max); // cores vivas e claras pesam mais
                r += pr * weight;
                g += pg * weight;
                b += pb * weight;
                total += weight;
            }
            if (total < 0.6) return fallback; // capa em tons de cinza: fica a cor de destaque
            r /= total;
            g /= total;
            b /= total;

            // Sobe o brilho até um nível confortável e garante saturação mínima.
            double hi = Math.Max(r, Math.Max(g, b));
            if (hi > 0)
            {
                double k = 0.92 / hi;
                r *= k;
                g *= k;
                b *= k;
            }
            // Evita cores lavadas: afasta os canais mais fracos do mais forte até ter contraste.
            const double top = 0.92, floor = 0.42;
            double lo = Math.Min(r, Math.Min(g, b));
            if (lo > floor && top - lo > 0.02)
            {
                double f = (top - floor) / (top - lo);
                r = top - (top - r) * f;
                g = top - (top - g) * f;
                b = top - (top - b) * f;
            }
            return Color.FromRgb(ToByte(r), ToByte(g), ToByte(b));

            static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }

        private void SetMediaAccent(Color color)
        {
            // No tema claro a cor precisa ser mais escura para aparecer sobre o fundo claro.
            if (!ThemePalette.IsDark)
                color = Color.FromRgb((byte)(color.R * 0.72), (byte)(color.G * 0.72), (byte)(color.B * 0.72));
            var from = (Resources["MediaAccent"] as SolidColorBrush)?.Color ?? color;
            var brush = new SolidColorBrush(from);
            Resources["MediaAccent"] = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(ReduceMotion ? 0 : 600)));
        }

        /// <summary>O brilho só aparece com a ilha aberta, com capa, e se a opção estiver ligada.</summary>
        private void UpdateAmbientVisibility(bool animate)
        {
            // Só no modo vidro: no sólido a ilha tem que ficar com a cor cheia, sem nada por cima.
            bool glass = string.Equals(_settings.Material, "Glass", StringComparison.OrdinalIgnoreCase);
            bool show = _expanded && glass && _ambientHasArt && _settings.AmbientArt && _settings.ShowMedia;
            double to = !show ? 0 : ThemePalette.IsDark ? 0.5 : 0.34;
            AmbientLayer.BeginAnimation(OpacityProperty,
                new DoubleAnimation(to, TimeSpan.FromMilliseconds(animate && !ReduceMotion ? (show ? 700 : 220) : 0)));
        }

        /// <summary>O brilho segue o formato exato da ilha (cantos arredondados).</summary>
        private void UpdateAmbientClip()
        {
            double w = Island.ActualWidth, h = Island.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double r = Island.CornerRadius.BottomLeft;
            double top = Detached ? 0 : -r; // colada: cantos de cima retos
            AmbientLayer.Clip = new RectangleGeometry(new Rect(0, top, w, h - top), r, r);
        }

        // ───────────── Entrada em cascata ─────────────

        /// <summary>Os blocos da página sobem e aparecem um depois do outro.</summary>
        private void StaggerIn(FrameworkElement page)
        {
            if (ReduceMotion) return;
            var blocks = (page as Panel)?.Children.OfType<FrameworkElement>()
                .Where(e => e.Visibility == Visibility.Visible).ToList();
            if (blocks == null || blocks.Count < 2) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                var delay = TimeSpan.FromMilliseconds(40 + i * 55);
                var rise = new TranslateTransform(0, 12);
                block.RenderTransform = rise;
                block.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = delay });
                rise.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(460)) { BeginTime = delay, EasingFunction = ease });
            }
        }

        private FrameworkElement? CurrentPage => AllPages.FirstOrDefault(p => p.Tab == _currentTab).Page;
    }
}
