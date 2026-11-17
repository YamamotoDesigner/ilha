using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Ilha
{
    /// <summary>
    /// Símbolos dos lembretes e da saudação: traço fino, uma cor só (mais um detalhe na cor de destaque).
    /// Cada um se desenha na hora em que aparece e depois fica com um movimento bem discreto.
    /// Cores pelo tema: "Ink_FF" (traço) e "Accent" (detalhe).
    /// </summary>
    public static class NudgeArt
    {
        private const double Size = 40, Stroke = 1.7;

        public static FrameworkElement Build(string kind) => kind switch
        {
            "sunrise" => Sunrise(),
            "sun" => Sun(),
            "moon" or "late" => Moon(),
            "water" => Drop(),
            "stretch" => Steps(),
            "alarm" => Bell(),
            "teeth" => Sparkle(),
            "clock" => Clock(),
            "here" => Here(),
            "hello" => GreetingFor(DateTime.Now.Hour),
            _ => Sun(),
        };

        /// <summary>Saudação: sol nascendo de manhã, sol à tarde, lua à noite.</summary>
        public static FrameworkElement GreetingFor(int hour) =>
            hour >= 5 && hour < 12 ? Sunrise() : hour >= 12 && hour < 18 ? Sun() : Moon();

        public static string GreetingKind(int hour) =>
            hour >= 5 && hour < 12 ? "sunrise" : hour >= 12 && hour < 18 ? "sun" : "moon";

        // ── Sol nascendo: a linha do horizonte, o sol sobe e os raios aparecem ──
        private static FrameworkElement Sunrise()
        {
            var c = Stage();
            var sunHost = new Canvas { Width = Size, Height = 28, ClipToBounds = true };
            var sun = Line(c: null, "M12 27 A8 8 0 0 1 28 27", accent: true);
            var rise = new TranslateTransform(0, 10);
            sun.RenderTransform = rise;
            sunHost.Children.Add(sun);
            c.Children.Add(sunHost);
            var horizon = Line(c, "M5 27.5 H35");
            var rays = Line(c, "M20 9.5 V12.5 M10.5 13.5 L12.6 15.6 M29.5 13.5 L27.4 15.6");
            var dashes = Line(c, "M10 32.5 H16 M20 32.5 H30");
            DrawOn(horizon, 0.5, 0);
            Animate(rise, TranslateTransform.YProperty, 10, 0, 0.9, 0.25);
            DrawOn(rays, 0.45, 0.9);
            DrawOn(dashes, 0.5, 0.4);
            Breathe(rays, 2.8, 1.4);
            return c;
        }

        // ── Sol: o círculo se desenha e os raios giram devagar ──
        private static FrameworkElement Sun()
        {
            var c = Stage();
            var core = Line(c, "M20 13.5 A6.5 6.5 0 1 1 19.99 13.5", accent: true);
            var rays = Line(c, "M20 5 V8.5 M20 31.5 V35 M5 20 H8.5 M31.5 20 H35 M9.4 9.4 L11.8 11.8 M28.2 28.2 L30.6 30.6 M9.4 30.6 L11.8 28.2 M28.2 11.8 L30.6 9.4");
            DrawOn(core, 0.7, 0);
            DrawOn(rays, 0.6, 0.45);
            var spin = new RotateTransform(0, 20, 20);
            rays.RenderTransform = spin;
            Loop(spin, RotateTransform.AngleProperty, 24, (0, 0), (1, 360), linear: true);
            return c;
        }

        // ── Lua: o crescente se desenha e duas estrelas piscam ──
        private static FrameworkElement Moon()
        {
            var c = Stage();
            var moon = Line(c, "M24.5 8.5 A12 12 0 1 0 31.5 26 A9.5 9.5 0 0 1 24.5 8.5 Z");
            DrawOn(moon, 0.9, 0);
            foreach (var (x, y, d) in new[] { (30.5, 9.5, 0.9), (34.5, 17.0, 1.4) })
            {
                var star = Dot(c, x, y, 1.6);
                star.Opacity = 0;
                Loop(star, UIElement.OpacityProperty, 2.6, (0, 0), (0.2, 1), (0.6, 0.25), (1, 0), delay: d);
            }
            var sway = new RotateTransform(0, 20, 20);
            moon.RenderTransform = sway;
            Loop(sway, RotateTransform.AngleProperty, 4, (0, -3), (0.5, 3), (1, -3), delay: 1);
            return c;
        }

        // ── Gota: o contorno se desenha e a água ondula por dentro ──
        private static FrameworkElement Drop()
        {
            var c = Stage();
            var shape = Geometry.Parse("M20 6 C20 6 10 17.5 10 24 A10 10 0 0 0 30 24 C30 17.5 20 6 20 6 Z");
            var waterHost = new Canvas { Width = Size, Height = Size, Clip = shape };
            var wave = new Path
            {
                Data = Geometry.Parse("M-20 26 Q-15 23.5 -10 26 T0 26 T10 26 T20 26 T30 26 T40 26 T50 26 T60 26 V40 H-20 Z"),
                Opacity = 0.9,
            };
            wave.SetResourceReference(Shape.FillProperty, "Accent");
            var flow = new TranslateTransform(0, 6);
            wave.RenderTransform = flow;
            waterHost.Children.Add(wave);
            c.Children.Add(waterHost);
            var outline = Line(c, "M20 6 C20 6 10 17.5 10 24 A10 10 0 0 0 30 24 C30 17.5 20 6 20 6 Z");
            DrawOn(outline, 0.8, 0);
            Animate(flow, TranslateTransform.YProperty, 14, 0, 1.0, 0.5);
            Loop(flow, TranslateTransform.XProperty, 1.6, (0, 0), (1, 20), linear: true);
            return c;
        }

        // ── Passos: pegadas aparecem uma de cada vez, subindo ──
        private static FrameworkElement Steps()
        {
            var c = Stage();
            var prints = new (double X, double Y)[] { (13, 30), (22, 24), (14, 17), (23, 11) };
            for (int i = 0; i < prints.Length; i++)
            {
                var (x, y) = prints[i];
                var foot = new Border { Width = 5, Height = 8, CornerRadius = new CornerRadius(2.5) };
                foot.SetResourceReference(Border.BackgroundProperty, i == prints.Length - 1 ? "Accent" : "Ink_FF");
                Canvas.SetLeft(foot, x);
                Canvas.SetTop(foot, y);
                foot.RenderTransform = new RotateTransform(i % 2 == 0 ? -12 : 12, 2.5, 4);
                foot.Opacity = 0;
                c.Children.Add(foot);
                double at = i * 0.16;
                Loop(foot, UIElement.OpacityProperty, 2.8, (0, 0), (at, 0), (at + 0.06, 1), (0.78, 1), (0.92, 0), (1, 0));
            }
            return c;
        }

        // ── Sino: se desenha e balança de leve ──
        private static FrameworkElement Bell()
        {
            var c = Stage();
            var bell = Line(c, "M12.5 27 V19.5 A7.5 7.5 0 0 1 27.5 19.5 V27 L29.5 29.5 H10.5 Z M20 9 V11.8");
            var clapper = Dot(c, 20, 32.4, 2.1, accent: true);
            DrawOn(bell, 0.8, 0);
            clapper.Opacity = 0;
            Animate(clapper, UIElement.OpacityProperty, 0, 1, 0.3, 0.7);
            var swing = new RotateTransform(0, 20, 10);
            bell.RenderTransform = swing;
            clapper.RenderTransform = swing;
            Loop(swing, RotateTransform.AngleProperty, 2.2, (0, 0), (0.08, -9), (0.18, 8), (0.28, -5), (0.36, 3), (0.44, 0), (1, 0), delay: 0.9);
            return c;
        }

        // ── Brilho: limpinho. Uma estrela de quatro pontas e outra menor piscando ──
        private static FrameworkElement Sparkle()
        {
            var c = Stage();
            var big = Line(c, "M18 8 Q19 18 28 19 Q19 20 18 30 Q17 20 8 19 Q17 18 18 8 Z");
            var small = Line(c, "M29 7 Q29.4 10.6 33 11 Q29.4 11.4 29 15 Q28.6 11.4 25 11 Q28.6 10.6 29 7 Z", accent: true);
            DrawOn(big, 0.8, 0);
            DrawOn(small, 0.5, 0.6);
            var pulse = new ScaleTransform(1, 1, 18, 19);
            big.RenderTransform = pulse;
            Loop(pulse, ScaleTransform.ScaleXProperty, 2.4, (0, 1), (0.5, 1.08), (1, 1), delay: 0.9);
            Loop(pulse, ScaleTransform.ScaleYProperty, 2.4, (0, 1), (0.5, 1.08), (1, 1), delay: 0.9);
            Loop(small, UIElement.OpacityProperty, 1.8, (0, 1), (0.5, 0.3), (1, 1), delay: 1.2);
            return c;
        }

        // ── Relógio: o mostrador se desenha, o ponteiro dos minutos dá uma volta inteira e para no 12 ──
        private static FrameworkElement Clock()
        {
            var c = Stage();
            var face = Line(c, "M20 7 A13 13 0 1 1 19.99 7");
            DrawOn(face, 0.8, 0);
            var ticks = Line(c, "M20 9.5 V11 M30.5 20 H29 M20 30.5 V29 M9.5 20 H11");
            DrawOn(ticks, 0.4, 0.5);
            var hourHand = Line(c, "M20 20 V14.5");
            var minuteHand = Line(c, "M20 20 V10.5", accent: true);
            int h = DateTime.Now.Hour % 12;
            var hourTurn = new RotateTransform(h * 30 - 30, 20, 20);
            var minTurn = new RotateTransform(0, 20, 20);
            hourHand.RenderTransform = hourTurn;
            minuteHand.RenderTransform = minTurn;
            Dot(c, 20, 20, 1.6);
            Animate(minTurn, RotateTransform.AngleProperty, 0, 360, 1.3, 0.55);
            Animate(hourTurn, RotateTransform.AngleProperty, h * 30 - 30, h * 30, 1.3, 0.55);
            return c;
        }

        // ── Cheguei: o marcador se desenha, o ponto aparece e um círculo se espalha embaixo ──
        private static FrameworkElement Here()
        {
            var c = Stage();
            var pin = Line(c, "M20 31 C20 31 11 22.5 11 16.5 A9 9 0 0 1 29 16.5 C29 22.5 20 31 20 31 Z");
            DrawOn(pin, 0.8, 0);
            var dot = Dot(c, 20, 16.5, 2.6, accent: true);
            dot.Opacity = 0;
            Animate(dot, UIElement.OpacityProperty, 0, 1, 0.3, 0.7);
            var ring = new Ellipse { Width = 16, Height = 5, StrokeThickness = 1.2 };
            ring.SetResourceReference(Shape.StrokeProperty, "Accent");
            Canvas.SetLeft(ring, 12);
            Canvas.SetTop(ring, 32);
            var grow = new ScaleTransform(0.2, 0.2, 8, 2.5);
            ring.RenderTransform = grow;
            ring.Opacity = 0;
            c.Children.Add(ring);
            Loop(grow, ScaleTransform.ScaleXProperty, 2.2, (0, 0.2), (0.6, 1.4), (1, 1.4), delay: 0.9);
            Loop(grow, ScaleTransform.ScaleYProperty, 2.2, (0, 0.2), (0.6, 1.4), (1, 1.4), delay: 0.9);
            Loop(ring, UIElement.OpacityProperty, 2.2, (0, 0.9), (0.6, 0), (1, 0), delay: 0.9);
            var bob = new TranslateTransform(0, 0);
            pin.RenderTransform = bob;
            Loop(bob, TranslateTransform.YProperty, 2.2, (0, 0), (0.25, -1.5), (0.5, 0), delay: 0.9);
            return c;
        }

        // ── Peças ──

        private static Canvas Stage() => new() { Width = Size, Height = Size };

        private static Path Line(Canvas? c, string data, bool accent = false)
        {
            var p = new Path
            {
                Data = Geometry.Parse(data), StrokeThickness = Stroke,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            };
            p.SetResourceReference(Shape.StrokeProperty, accent ? "Accent" : "Ink_FF");
            c?.Children.Add(p);
            return p;
        }

        private static Ellipse Dot(Canvas c, double x, double y, double r, bool accent = false)
        {
            var e = new Ellipse { Width = r * 2, Height = r * 2 };
            e.SetResourceReference(Shape.FillProperty, accent ? "Accent" : "Ink_FF");
            Canvas.SetLeft(e, x - r);
            Canvas.SetTop(e, y - r);
            c.Children.Add(e);
            return e;
        }

        /// <summary>O traço "se desenha" do começo ao fim.</summary>
        private static void DrawOn(Path p, double seconds, double delay)
        {
            double len = Length(p.Data) / p.StrokeThickness + 2;
            p.StrokeDashArray = new DoubleCollection { len, len };
            p.StrokeDashOffset = len;
            if (!SystemParameters.ClientAreaAnimation) { p.StrokeDashOffset = 0; return; }
            p.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(len, 0, TimeSpan.FromSeconds(seconds))
            {
                BeginTime = TimeSpan.FromSeconds(delay),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd,
            });
        }

        private static double Length(Geometry g)
        {
            var flat = g.GetFlattenedPathGeometry(0.1, ToleranceType.Absolute);
            double total = 0;
            foreach (var fig in flat.Figures)
            {
                var prev = fig.StartPoint;
                var start = prev;
                foreach (var seg in fig.Segments)
                {
                    var pts = seg switch
                    {
                        PolyLineSegment pl => pl.Points.ToArray(),
                        LineSegment l => new[] { l.Point },
                        _ => Array.Empty<Point>(),
                    };
                    foreach (var pt in pts) { total += (pt - prev).Length; prev = pt; }
                }
                if (fig.IsClosed) total += (start - prev).Length;
            }
            return total;
        }

        private static void Animate(IAnimatable t, DependencyProperty p, double from, double to, double seconds, double delay) =>
            t.BeginAnimation(p, new DoubleAnimation(from, to, TimeSpan.FromSeconds(SystemParameters.ClientAreaAnimation ? seconds : 0))
            {
                BeginTime = TimeSpan.FromSeconds(delay),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

        private static void Breathe(UIElement e, double seconds, double delay) =>
            Loop(e, UIElement.OpacityProperty, seconds, (0, 1), (0.5, 0.45), (1, 1), delay: delay);

        private static void Loop(IAnimatable target, DependencyProperty p, double seconds, params (double T, double V)[] keys) =>
            Loop(target, p, seconds, keys, false, 0);

        private static void Loop(IAnimatable target, DependencyProperty p, double seconds, (double, double) k1, (double, double) k2, bool linear) =>
            Loop(target, p, seconds, new[] { k1, k2 }, linear, 0);

        private static void Loop(IAnimatable target, DependencyProperty p, double seconds, (double, double) k1, (double, double) k2, (double, double) k3, double delay) =>
            Loop(target, p, seconds, new[] { k1, k2, k3 }, false, delay);

        private static void Loop(IAnimatable target, DependencyProperty p, double seconds, (double, double) k1, (double, double) k2, (double, double) k3, (double, double) k4, double delay) =>
            Loop(target, p, seconds, new[] { k1, k2, k3, k4 }, false, delay);

        private static void Loop(IAnimatable target, DependencyProperty p, double seconds, (double, double) k1, (double, double) k2, (double, double) k3,
                                 (double, double) k4, (double, double) k5, (double, double) k6, (double, double) k7, double delay) =>
            Loop(target, p, seconds, new[] { k1, k2, k3, k4, k5, k6, k7 }, false, delay);

        private static void Loop(IAnimatable target, DependencyProperty p, double seconds, (double T, double V)[] keys, bool linear, double delay)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var a = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay),
            };
            foreach (var (t, v) in keys)
                a.KeyFrames.Add(linear
                    ? new LinearDoubleKeyFrame(v, KeyTime.FromPercent(t))
                    : new EasingDoubleKeyFrame(v, KeyTime.FromPercent(t), new SineEase { EasingMode = EasingMode.EaseInOut }));
            target.BeginAnimation(p, a);
        }
    }
}
