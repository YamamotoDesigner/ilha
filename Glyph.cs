using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Ilha
{
    /// <summary>
    /// Ícone vetorial desenhado em uma grade de 24×24 (ver Theme.xaml). Herda a cor do texto,
    /// então funciona dentro de qualquer botão sem estilo extra.
    /// </summary>
    public sealed class Glyph : FrameworkElement
    {
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(Geometry), typeof(Glyph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
            nameof(Size), typeof(double), typeof(Glyph),
            new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
            nameof(Filled), typeof(bool), typeof(Glyph),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Espessura do traço em unidades da grade de 24 (2 ≈ 1,3 px num ícone de 16 px).</summary>
        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
            nameof(Thickness), typeof(double), typeof(Glyph),
            new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty =
            TextElement.ForegroundProperty.AddOwner(typeof(Glyph),
                new FrameworkPropertyMetadata(Brushes.White,
                    FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

        public Geometry? Data { get => (Geometry?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
        public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
        public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
        public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
        public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

        protected override System.Windows.Size MeasureOverride(System.Windows.Size available) => new(Size, Size);

        protected override void OnRender(DrawingContext dc)
        {
            if (Data == null) return;
            double scale = Size / 24.0;
            var pen = new Pen(Foreground, Filled ? 2.4 : Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            dc.PushTransform(new TranslateTransform((ActualWidth - Size) / 2, (ActualHeight - Size) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(Filled ? Foreground : null, pen, Data);
            dc.Pop();
            dc.Pop();
        }
    }
}
