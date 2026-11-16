using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Ilha
{
    /// <summary>
    /// Ícones animados dos lembretes (carinha, copo d'água, pessoa andando, lua, despertador e escova).
    /// Desenhados em vetor, 40 × 40, com animação em loop.
    /// </summary>
    public static class NudgeArt
    {
        public static readonly Color Yellow = Color.FromRgb(0xFF, 0xD6, 0x0A);
        public static readonly Color Water = Color.FromRgb(0x5A, 0xC8, 0xFA);
        public static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58);
        public static readonly Color Moon = Color.FromRgb(0xFF, 0xE0, 0x8A);
        public static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);
        public static readonly Color Mint = Color.FromRgb(0x63, 0xE6, 0xE2);

        public static FrameworkElement Build(string kind) => kind switch
        {
            "hello" => Smiley(),
            "water" => Glass(),
            "stretch" => Walker(),
            "late" => Sleepy(),
            "alarm" => AlarmClock(),
            "teeth" => Toothbrush(),
            _ => Smiley(),
        };

        // ── Carinha feliz: pula de leve e dá uma piscadinha ──
        private static FrameworkElement Smiley()
        {
            var c = Stage();
            var face = new Ellipse { Width = 34, Height = 34, Fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0xE2, 0x4D), Color.FromRgb(0xFF, 0xB8, 0x00), 90) };
            Place(c, face, 3, 3);
            var leftEye = new Ellipse { Width = 4.2, Height = 5.6, Fill = Brush(0x3A, 0x26, 0x00), RenderTransformOrigin = new Point(0.5, 0.5) };
            var rightEye = new Ellipse { Width = 4.2, Height = 5.6, Fill = Brush(0x3A, 0x26, 0x00), RenderTransformOrigin = new Point(0.5, 0.5) };
            Place(c, leftEye, 13, 13);
            Place(c, rightEye, 22.8, 13);
            var smile = new Path { Data = Geometry.Parse("M12 22.5 Q20 30.5 28 22.5"), Stroke = Brush(0x3A, 0x26, 0x00), StrokeThickness = 2.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            c.Children.Add(smile);
            var cheekL = new Ellipse { Width = 5, Height = 3, Fill = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x6B, 0x4A)) };
            var cheekR = new Ellipse { Width = 5, Height = 3, Fill = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x6B, 0x4A)) };
            Place(c, cheekL, 8, 21);
            Place(c, cheekR, 27, 21);
            // Pulinho
            var bounce = new TranslateTransform();
            var scale = new ScaleTransform(1, 1, 20, 36);
            c.RenderTransform = new TransformGroup { Children = { scale, bounce } };
            Loop(bounce, TranslateTransform.YProperty, 2.4, (0, 0), (0.12, -4), (0.24, 0), (0.3, -1.5), (0.36, 0), (1, 0));
            Loop(scale, ScaleTransform.ScaleYProperty, 2.4, (0, 1), (0.1, 1.04), (0.22, 0.94), (0.3, 1), (1, 1));
            // Piscadinha (olho direito)
            var wink = new ScaleTransform(1, 1);
            rightEye.RenderTransform = wink;
            Loop(wink, ScaleTransform.ScaleYProperty, 2.4, (0, 1), (0.55, 1), (0.6, 0.12), (0.68, 1), (1, 1));
            return c;
        }

        // ── Copo d'água: enche devagar e cai uma gotinha ──
        private static FrameworkElement Glass()
        {
            var c = Stage();
            var glassShape = Geometry.Parse("M10 9 L30 9 L27.5 35 Q27.3 37 25 37 L15 37 Q12.7 37 12.5 35 Z");
            var water = new Rectangle { Width = 22, Height = 30, Fill = new LinearGradientBrush(Color.FromRgb(0x8E, 0xE2, 0xFF), Water, 90) };
            var level = new TranslateTransform(0, 18);
            water.RenderTransform = level;
            var waterHost = new Canvas { Width = 40, Height = 40, Clip = glassShape };
            Place(waterHost, water, 9, 9);
            c.Children.Add(waterHost);
            c.Children.Add(new Path { Data = glassShape, Stroke = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round });
            Loop(level, TranslateTransform.YProperty, 3.2, (0, 20), (0.7, 7), (0.9, 7), (1, 20));
            var drop = new Path { Data = Geometry.Parse("M0 4 Q0 0 2 -2 Q4 0 4 4 A2 2 0 0 1 0 4 Z"), Fill = new SolidColorBrush(Water) };
            var fall = new TranslateTransform(18, -2);
            drop.RenderTransform = fall;
            c.Children.Add(drop);
            Loop(fall, TranslateTransform.YProperty, 3.2, (0, -4), (0.3, -4), (0.55, 16), (1, 16));
            Loop(drop, UIElement.OpacityProperty, 3.2, (0, 0), (0.3, 1), (0.52, 1), (0.56, 0), (1, 0));
            return c;
        }

        // ── Pessoa andando: pernas e braços balançam ──
        private static FrameworkElement Walker()
        {
            var c = Stage();
            var ink = new SolidColorBrush(Green);
            Place(c, new Ellipse { Width = 7.5, Height = 7.5, Fill = ink }, 16.5, 4);
            Line(c, 20, 13, 20, 24, ink, 3.2);
            var legL = Limb(c, 20, 24, 20, 35, ink);
            var legR = Limb(c, 20, 24, 20, 35, ink);
            var armL = Limb(c, 20, 15, 20, 23.5, ink);
            var armR = Limb(c, 20, 15, 20, 23.5, ink);
            Swing(legL, 20, 24, 28, 1.0, 0);
            Swing(legR, 20, 24, 28, 1.0, 0.5);
            Swing(armL, 20, 15, 34, 1.0, 0.5);
            Swing(armR, 20, 15, 34, 1.0, 0);
            var step = new TranslateTransform();
            c.RenderTransform = step;
            Loop(step, TranslateTransform.YProperty, 0.5, (0, 0), (0.5, -1.2), (1, 0));
            return c;
        }

        // ── Lua sonolenta com "z" subindo ──
        private static FrameworkElement Sleepy()
        {
            var c = Stage();
            var moon = new Path
            {
                Data = Geometry.Parse("M24 6 A14 14 0 1 0 34 26 A11 11 0 1 1 24 6 Z"),
                Fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0xEC, 0xB0), Moon, 90),
            };
            var sway = new RotateTransform(0, 20, 22);
            moon.RenderTransform = sway;
            c.Children.Add(moon);
            Loop(sway, RotateTransform.AngleProperty, 3, (0, -6), (0.5, 6), (1, -6));
            for (int i = 0; i < 3; i++)
            {
                var z = new TextBlock { Text = "z", FontSize = 8 + i * 2.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0xC7, 0xFF)) };
                var move = new TranslateTransform(26 + i * 3, 14);
                z.RenderTransform = move;
                c.Children.Add(z);
                double off = i * 0.25;
                Loop(move, TranslateTransform.YProperty, 2.4, (0, 18 - off * 4), (off, 18 - off * 4), (off + 0.55, -2 - i * 2), (1, -2 - i * 2));
                Loop(z, UIElement.OpacityProperty, 2.4, (0, 0), (off, 0), (off + 0.1, 1), (off + 0.5, 0), (1, 0));
            }
            return c;
        }

        // ── Despertador que treme ──
        private static FrameworkElement AlarmClock()
        {
            var c = Stage();
            var body = new Canvas { Width = 40, Height = 40 };
            var orange = new SolidColorBrush(Orange);
            Place(body, new Ellipse { Width = 27, Height = 27, Fill = orange }, 6.5, 9);
            Place(body, new Ellipse { Width = 21, Height = 21, Fill = Brush(0xFF, 0xF7, 0xE8) }, 9.5, 12);
            body.Children.Add(new Path { Data = Geometry.Parse("M8 9 A6 6 0 0 1 15 6 Z M32 9 A6 6 0 0 0 25 6 Z"), Fill = orange });
            Line(body, 20, 22.5, 20, 15.5, Brush(0x33, 0x22, 0x10), 2);
            Line(body, 20, 22.5, 24.5, 24.5, Brush(0x33, 0x22, 0x10), 2);
            Line(body, 12, 34, 10, 37, orange, 2.4);
            Line(body, 28, 34, 30, 37, orange, 2.4);
            var shake = new RotateTransform(0, 20, 22);
            body.RenderTransform = shake;
            c.Children.Add(body);
            Loop(shake, RotateTransform.AngleProperty, 1.6, (0, 0), (0.05, -14), (0.1, 14), (0.15, -12), (0.2, 12), (0.25, -8), (0.3, 8), (0.36, 0), (1, 0));
            return c;
        }

        // ── Escova indo e voltando, com bolhinhas ──
        private static FrameworkElement Toothbrush()
        {
            var c = Stage();
            var brush = new Canvas { Width = 40, Height = 40 };
            var handle = new Border { Width = 30, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Mint) };
            Place(brush, handle, 5, 22);
            var head = new Border { Width = 11, Height = 7, CornerRadius = new CornerRadius(1.5), Background = Brush(0xF2, 0xF2, 0xF7) };
            Place(brush, head, 24, 15);
            var move = new TranslateTransform();
            brush.RenderTransform = new TransformGroup { Children = { new RotateTransform(-18, 20, 22), move } };
            c.Children.Add(brush);
            Loop(move, TranslateTransform.XProperty, 0.7, (0, -3), (0.5, 3), (1, -3));
            for (int i = 0; i < 3; i++)
            {
                var b = new Ellipse { Width = 4 + i, Height = 4 + i, Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 };
                var up = new TranslateTransform(28 + i * 3, 10);
                b.RenderTransform = up;
                c.Children.Add(b);
                double off = i * 0.3;
                Loop(up, TranslateTransform.YProperty, 1.8, (0, 12), (off, 12), (off + 0.5, 0), (1, 0));
                Loop(b, UIElement.OpacityProperty, 1.8, (0, 0), (off, 0), (off + 0.1, 1), (off + 0.5, 0), (1, 0));
            }
            return c;
        }

        // ── Peças ──

        private static Canvas Stage() => new() { Width = 40, Height = 40, ClipToBounds = false };

        private static void Place(Canvas c, UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            c.Children.Add(e);
        }

        private static void Line(Canvas c, double x1, double y1, double x2, double y2, Brush b, double t) =>
            c.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = t, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });

        private static Line Limb(Canvas c, double x1, double y1, double x2, double y2, Brush b)
        {
            var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            c.Children.Add(l);
            return l;
        }

        private static void Swing(UIElement e, double cx, double cy, double angle, double seconds, double phase)
        {
            var r = new RotateTransform(0, cx, cy);
            e.RenderTransform = r;
            double a = phase < 0.25 ? angle : -angle;
            Loop(r, RotateTransform.AngleProperty, seconds, (0, a), (0.5, -a), (1, a));
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

        private static void Loop(IAnimatable target, DependencyProperty p, double seconds, params (double T, double V)[] keys)
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever };
            foreach (var (t, v) in keys)
                a.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromPercent(Math.Clamp(t, 0, 1)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            target.BeginAnimation(p, a);
        }
    }
}
