using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Ilha
{
    /// <summary>
    /// Ilustrações animadas da tela "Novidades": uma mini ilha fazendo, em loop, aquilo que a página
    /// explica (abrir, rolar os dias, marcar tarefas, arrastar a música...). Tudo em vetor.
    /// </summary>
    public static class WhatsNewArt
    {
        public const double W = 440, H = 216;

        /// <summary>Nome usado na cena da saudação.</summary>
        public static string UserName { get; set; } = "";

        private static readonly Brush IslandFill = Frozen(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0C)));
        private static readonly Brush IslandRim = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush Card = Frozen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush Line1 = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush Line2 = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush Line3 = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));

        public static FrameworkElement Build(string key, Color accent)
        {
            var a = new SolidColorBrush(accent);
            FrameworkElement art = key switch
            {
                "intro" => Intro(a),
                "full" => Full(a),
                "notes" => Notes(),
                "agenda" => Agenda(a),
                "tasks" => Tasks(a),
                "music" => Music(a),
                "dynamic" => Dynamic(a),
                "hello" => Hello(),
                "wellness" => Wellness(),
                _ => Icon(a),
            };
            // Palco: um cartão suave com um brilho da cor de destaque atrás da ilha.
            var glow = new RadialGradientBrush(Color.FromArgb(0x38, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B))
            {
                Center = new Point(0.5, 0.35), GradientOrigin = new Point(0.5, 0.35), RadiusX = 0.6, RadiusY = 0.8,
            };
            var stage = new Border { Width = W, Height = H, CornerRadius = new CornerRadius(22), ClipToBounds = true, BorderThickness = new Thickness(1) };
            stage.SetResourceReference(Border.BackgroundProperty, "CardBg");
            stage.SetResourceReference(Border.BorderBrushProperty, "Line");
            stage.Child = new Grid { Children = { new Border { Background = glow }, art } };
            return stage;
        }

        // ───────────── Cenas ─────────────

        /// <summary>A ilha fechada no topo abre, mostra a música e fecha de novo.</summary>
        private static FrameworkElement Intro(Brush accent)
        {
            const double d = 5;
            var stage = new Grid();
            var island = Island(130, 28, top: 0);
            island.CornerRadius = new CornerRadius(0, 0, 14, 14);
            island.VerticalAlignment = VerticalAlignment.Top;
            island.Margin = new Thickness(0, 0, 0, 0);
            Anim(island, FrameworkElement.WidthProperty, d, (0, 130), (0.16, 130), (0.34, 330), (0.8, 330), (0.96, 130), (1, 130));
            Anim(island, FrameworkElement.HeightProperty, d, (0, 28), (0.16, 28), (0.34, 150), (0.8, 150), (0.96, 28), (1, 28));

            // Fechada: só a capa pequena e as barrinhas
            var mini = new Grid { Height = 28, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 10, 0) };
            mini.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Background = ArtBrush(), HorizontalAlignment = HorizontalAlignment.Left });
            mini.Children.Add(Bars(accent, HorizontalAlignment.Right));
            Anim(mini, UIElement.OpacityProperty, d, (0, 1), (0.18, 1), (0.24, 0), (0.88, 0), (0.95, 1), (1, 1));

            // Aberta: capa grande, título, progresso, controles
            var open = new Grid { Margin = new Thickness(18, 22, 18, 16), Opacity = 0 };
            open.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            open.ColumnDefinitions.Add(new ColumnDefinition());
            open.Children.Add(new Border { Width = 92, Height = 92, CornerRadius = new CornerRadius(16), Background = ArtBrush(), VerticalAlignment = VerticalAlignment.Center });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            info.Children.Add(Bar(120, 9, Line1));
            info.Children.Add(Spaced(Bar(80, 7, Line2), 7));
            var progress = new Grid { Height = 3, Margin = new Thickness(0, 18, 0, 0) };
            progress.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = Line3 });
            var fill = new Border { CornerRadius = new CornerRadius(1.5), Background = Line1, HorizontalAlignment = HorizontalAlignment.Left, Width = 40 };
            Anim(fill, FrameworkElement.WidthProperty, d, (0, 30), (0.3, 30), (0.85, 110), (1, 110));
            progress.Children.Add(fill);
            info.Children.Add(progress);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            controls.Children.Add(Glyphy("IcoPrev", 12, Line2, true));
            controls.Children.Add(Spaced(Glyphy("IcoPause", 16, Line1, true), 0, 18));
            controls.Children.Add(Spaced(Glyphy("IcoNext", 12, Line2, true), 0, 18));
            info.Children.Add(controls);
            Grid.SetColumn(info, 1);
            open.Children.Add(info);
            Anim(open, UIElement.OpacityProperty, d, (0, 0), (0.3, 0), (0.42, 1), (0.76, 1), (0.84, 0), (1, 0));

            island.Child = new Grid { Children = { mini, open } };
            stage.Children.Add(island);
            stage.Children.Add(Cursor(d, (0, 300, 150), (0.08, 230, 40), (0.6, 230, 60), (0.8, 330, 190), (1, 300, 150)));
            return stage;
        }

        /// <summary>Uma aba vira a ilha completa: os blocos vão aparecendo lado a lado.</summary>
        private static FrameworkElement Full(Brush accent)
        {
            const double d = 5;
            var stage = new Grid();
            var island = Island(150, 118, top: 48);
            Anim(island, FrameworkElement.WidthProperty, d, (0, 150), (0.18, 150), (0.42, 404), (0.82, 404), (0.96, 150), (1, 150));
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            FrameworkElement[] blocks =
            {
                Block(new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(9), Background = ArtBrush() }),
                Block(new StackPanel { Children = { Txt("15", 22, accent, true), Spaced(Bar(30, 4, Line3), 6) } }),
                Block(new StackPanel { Children = { CheckRow(accent, true), Spaced(CheckRow(accent, false), 6), Spaced(CheckRow(accent, false), 6) } }),
                Block(new StackPanel { Children = { new Border { Width = 34, Height = 10, CornerRadius = new CornerRadius(3), Background = Brush(0xFF, 0xD6, 0x0A) }, Spaced(Bar(40, 4, Line2), 7), Spaced(Bar(28, 4, Line3), 5) } }),
                Block(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(3), BorderBrush = accent, Child = Txt("25", 12, Line1, false, center: true) }),
            };
            for (int i = 0; i < blocks.Length; i++)
            {
                if (i > 0)
                {
                    double t = 0.28 + i * 0.05;
                    Anim(blocks[i], UIElement.OpacityProperty, d, (0, 0), (t, 0), (t + 0.08, 1), (0.8, 1), (0.86, 0), (1, 0));
                    var s = new ScaleTransform(1, 1, 33, 40);
                    blocks[i].RenderTransform = s;
                    Anim(s, ScaleTransform.ScaleXProperty, d, (0, 0.7), (t, 0.7), (t + 0.1, 1), (1, 1));
                    Anim(s, ScaleTransform.ScaleYProperty, d, (0, 0.7), (t, 0.7), (t + 0.1, 1), (1, 1));
                }
                row.Children.Add(blocks[i]);
            }
            island.Child = row;
            stage.Children.Add(island);
            return stage;
        }

        /// <summary>O + cria uma página nova, colorida, ao lado das outras.</summary>
        private static FrameworkElement Notes()
        {
            const double d = 4.6;
            var stage = new Grid();
            var island = Island(372, 160, top: 28);
            var grid = new Grid { Margin = new Thickness(18, 14, 18, 14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var plus = new Grid { Width = 24, Height = 24, RenderTransformOrigin = new Point(0.5, 0.5) };
            plus.Children.Add(new Ellipse { Fill = Brush(0x40, 0xFF, 0xFF, 0xFF) });
            plus.Children.Add(Glyphy("IcoPlus", 12, Line1, false));
            var ps = new ScaleTransform(1, 1);
            plus.RenderTransform = ps;
            Anim(ps, ScaleTransform.ScaleXProperty, d, (0, 1), (0.14, 1), (0.2, 0.8), (0.28, 1), (1, 1));
            Anim(ps, ScaleTransform.ScaleYProperty, d, (0, 1), (0.14, 1), (0.2, 0.8), (0.28, 1), (1, 1));
            DockPanel.SetDock(plus, Dock.Right);
            header.Children.Add(plus);
            header.Children.Add(Txt(L.T("PÁGINAS"), 9.5, Line2, true));
            grid.Children.Add(header);

            var cards = new WrapPanel { Width = 336 };
            cards.Children.Add(Page(L.T("Mercado"), Color.FromRgb(0x30, 0xD1, 0x58)));
            cards.Children.Add(Page(L.T("Ideias"), Color.FromRgb(0xBF, 0x5A, 0xF2)));
            var fresh = Page(L.T("Viagem"), Color.FromRgb(0xFF, 0x9F, 0x0A));
            var fs = new ScaleTransform(1, 1, 80, 22);
            fresh.RenderTransform = fs;
            Anim(fresh, UIElement.OpacityProperty, d, (0, 0), (0.26, 0), (0.36, 1), (0.86, 1), (0.94, 0), (1, 0));
            Anim(fs, ScaleTransform.ScaleXProperty, d, (0, 0.6), (0.26, 0.6), (0.4, 1), (1, 1));
            Anim(fs, ScaleTransform.ScaleYProperty, d, (0, 0.6), (0.26, 0.6), (0.4, 1), (1, 1));
            cards.Children.Add(fresh);
            // Formatação: B I U acendendo uma de cada vez
            var format = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 12, 0, 0) };
            string[] marks = { "B", "I", "U" };
            for (int i = 0; i < 3; i++)
            {
                var chip = new Border { Width = 24, Height = 22, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 4, 0), Background = Brush(0x40, 0xFF, 0xFF, 0xFF), Opacity = 0.25 };
                var t = new TextBlock { Text = marks[i], FontSize = 12, Foreground = Line1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                if (i == 0) t.FontWeight = FontWeights.Bold;
                if (i == 1) { t.FontStyle = FontStyles.Italic; t.FontFamily = new FontFamily("Georgia"); }
                if (i == 2) t.TextDecorations = TextDecorations.Underline;
                chip.Child = t;
                double s = 0.46 + i * 0.1;
                Anim(chip, UIElement.OpacityProperty, d, (0, 0.25), (s, 0.25), (s + 0.05, 1), (s + 0.14, 1), (s + 0.2, 0.25), (1, 0.25));
                format.Children.Add(chip);
            }
            cards.Children.Add(format);
            Grid.SetRow(cards, 1);
            grid.Children.Add(cards);
            island.Child = grid;
            stage.Children.Add(island);
            stage.Children.Add(Cursor(d, (0, 330, 170), (0.12, 362, 48), (0.3, 362, 48), (0.6, 120, 150), (1, 330, 170)));
            return stage;
        }

        /// <summary>Os dias passam de lado; o escolhido cresce e o lembrete daquele dia aparece.</summary>
        private static FrameworkElement Agenda(Brush accent)
        {
            const double d = 5;
            const double cell = 38;
            var stage = new Grid();
            var island = Island(380, 156, top: 30);
            var root = new Grid { Margin = new Thickness(18, 16, 18, 14) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            root.RowDefinitions.Add(new RowDefinition());
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            var month = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            month.Children.Add(Txt(L.T("Nov"), 21, Line1, true));
            month.Children.Add(Txt("2026", 9, Line2, false));
            root.Children.Add(month);

            // Faixa de números (cinza) que desliza um dia por vez
            var viewport = new Canvas { ClipToBounds = true, Height = 56 };
            viewport.OpacityMask = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.2), new GradientStop(Colors.Black, 0.8), new GradientStop(Colors.Transparent, 1) },
            };
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            for (int day = 8; day <= 26; day++)
            {
                var c = new StackPanel { Width = cell };
                c.Children.Add(new TextBlock { Text = Week(day), FontSize = 8.5, Foreground = Line3, HorizontalAlignment = HorizontalAlignment.Center });
                c.Children.Add(new TextBlock { Text = day.ToString(), FontSize = 14, Foreground = Line2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                strip.Children.Add(c);
            }
            var shift = new TranslateTransform();
            strip.RenderTransform = shift;
            // Centro da faixa = 278 / 2; dia 15 é o índice 7.
            double x0 = 139 - (7 * cell + cell / 2);
            var keys = new (double, double)[]
            {
                (0, x0), (0.14, x0), (0.24, x0 - cell), (0.38, x0 - cell), (0.48, x0 - 2 * cell), (0.84, x0 - 2 * cell), (0.96, x0), (1, x0),
            };
            Anim(shift, TranslateTransform.XProperty, d, keys);
            viewport.Children.Add(strip);
            // O dia do centro, grande e colorido (troca a cada passo)
            var center = new Grid { Width = cell + 6, HorizontalAlignment = HorizontalAlignment.Center, Background = IslandFill };
            var labels = new (string Day, string Week, double From, double To)[] { ("15", Week(15), 0, 0.2), ("16", Week(16), 0.2, 0.44), ("17", Week(17), 0.44, 0.9), ("15", Week(15), 0.9, 1.01) };
            foreach (var (dayText, week, from, to) in labels)
            {
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = week, FontSize = 8.5, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = dayText, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) });
                sp.Opacity = 0;
                if (from == 0) Anim(sp, UIElement.OpacityProperty, d, (0, 1), (to - 0.02, 1), (to, 0), (1, 0));
                else if (to > 1) Anim(sp, UIElement.OpacityProperty, d, (0, 0), (from, 0), (from + 0.03, 1), (1, 1));
                else Anim(sp, UIElement.OpacityProperty, d, (0, 0), (from, 0), (from + 0.03, 1), (to - 0.02, 1), (to, 0), (1, 0));
                center.Children.Add(sp);
            }
            var stripHost = new Grid { Children = { viewport, center } };
            Grid.SetColumn(stripHost, 1);
            root.Children.Add(stripHost);

            // Lembrete do dia 17
            var reminder = new Border { CornerRadius = new CornerRadius(10), Background = Card, Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 8, 0, 0) };
            var rr = new StackPanel { Orientation = Orientation.Horizontal };
            rr.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) });
            var rt = Txt(L.T("Dentista · 15h"), 11.5, Line1, false);
            rt.VerticalAlignment = VerticalAlignment.Center;
            rr.Children.Add(rt);
            reminder.Child = rr;
            reminder.VerticalAlignment = VerticalAlignment.Top;
            Anim(reminder, UIElement.OpacityProperty, d, (0, 0), (0.5, 0), (0.58, 1), (0.86, 1), (0.92, 0), (1, 0));
            Grid.SetRow(reminder, 1);
            Grid.SetColumnSpan(reminder, 2);
            var nothing = Txt(L.T("Nada marcado para este dia."), 11.5, Line3, false);
            nothing.Margin = new Thickness(2, 18, 0, 0);
            Anim(nothing, UIElement.OpacityProperty, d, (0, 1), (0.46, 1), (0.5, 0), (0.9, 0), (0.94, 1), (1, 1));
            Grid.SetRow(nothing, 1);
            Grid.SetColumnSpan(nothing, 2);
            root.Children.Add(nothing);
            root.Children.Add(reminder);
            island.Child = root;
            stage.Children.Add(island);
            return stage;
        }

        /// <summary>As tarefas vão sendo marcadas e a barrinha enche.</summary>
        private static FrameworkElement Tasks(Brush accent)
        {
            const double d = 4.6;
            var stage = new Grid();
            var island = Island(300, 160, top: 28);
            var col = new StackPanel { Margin = new Thickness(20, 16, 20, 14) };
            var head = new Grid();
            head.Children.Add(Txt(L.T("TAREFAS DE HOJE"), 9, Line2, true));
            var counter = new Grid { HorizontalAlignment = HorizontalAlignment.Right };
            double[] at = { 0.2, 0.38, 0.56 };
            for (int i = 0; i <= 3; i++)
            {
                var t = Txt($"{i}/3", 9, i == 3 ? accent : Line2, true);
                if (i == 0) Anim(t, UIElement.OpacityProperty, d, (0, 1), (at[0], 1), (at[0] + 0.01, 0), (0.92, 0), (0.93, 1), (1, 1));
                else
                {
                    double from = at[i - 1], to = i == 3 ? 0.92 : at[i];
                    Anim(t, UIElement.OpacityProperty, d, (0, 0), (from, 0), (from + 0.01, 1), (to, 1), (to + 0.01, 0), (1, 0));
                }
                counter.Children.Add(t);
            }
            head.Children.Add(counter);
            col.Children.Add(head);
            var track = new Grid { Height = 3, Margin = new Thickness(0, 9, 0, 8) };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = Line3 });
            var fill = new Border { CornerRadius = new CornerRadius(1.5), Background = accent, HorizontalAlignment = HorizontalAlignment.Left };
            Anim(fill, FrameworkElement.WidthProperty, d, (0, 0), (0.2, 0), (0.25, 87), (0.38, 87), (0.43, 174), (0.56, 174), (0.61, 260), (0.88, 260), (0.94, 0), (1, 0));
            track.Children.Add(fill);
            col.Children.Add(track);
            string[] texts = { L.T("Responder e-mails"), L.T("Revisar a apresentação"), L.T("Caminhar 30 minutos") };
            for (int i = 0; i < 3; i++)
            {
                double t = at[i];
                var box = new Grid { Width = 15, Height = 15, Margin = new Thickness(0, 0, 10, 0) };
                box.Children.Add(new Border { CornerRadius = new CornerRadius(4.5), BorderBrush = Line2, BorderThickness = new Thickness(1.4) });
                var on = new Border { CornerRadius = new CornerRadius(4.5), Background = accent, Opacity = 0, Child = Glyphy("IcoCheck", 12, Brush(0xFF, 0x11, 0x11, 0x11), false, 3) };
                var sc = new ScaleTransform(1, 1, 7.5, 7.5);
                on.RenderTransform = sc;
                Anim(on, UIElement.OpacityProperty, d, (0, 0), (t, 0), (t + 0.03, 1), (0.88, 1), (0.92, 0), (1, 0));
                Anim(sc, ScaleTransform.ScaleXProperty, d, (0, 0.5), (t, 0.5), (t + 0.06, 1), (1, 1));
                Anim(sc, ScaleTransform.ScaleYProperty, d, (0, 0.5), (t, 0.5), (t + 0.06, 1), (1, 1));
                box.Children.Add(on);
                var label = Txt(texts[i], 12, Line1, false);
                Anim(label, UIElement.OpacityProperty, d, (0, 1), (t, 1), (t + 0.04, 0.4), (0.88, 0.4), (0.92, 1), (1, 1));
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { box, label } };
                col.Children.Add(row);
            }
            island.Child = col;
            stage.Children.Add(island);
            stage.Children.Add(Cursor(d, (0, 120, 200), (0.16, 92, 106), (0.22, 92, 106), (0.34, 92, 128), (0.4, 92, 128), (0.52, 92, 150), (0.6, 92, 150), (0.8, 200, 200), (1, 120, 200)));
            return stage;
        }

        /// <summary>O cursor segura a barra da música e arrasta até o ponto exato.</summary>
        private static FrameworkElement Music(Brush accent)
        {
            const double d = 4.8;
            var stage = new Grid();
            var island = Island(360, 132, top: 40);
            var g = new Grid { Margin = new Thickness(18, 16, 18, 16) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(16), Background = ArtBrush() });
            var info = new StackPanel { Margin = new Thickness(16, 6, 0, 0) };
            info.Children.Add(Bar(110, 9, Line1));
            info.Children.Add(Spaced(Bar(70, 7, Line2), 7));
            var progress = new Grid { Height = 14, Width = 196, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
            var track = new Border { CornerRadius = new CornerRadius(2), Background = Line3, Height = 3, VerticalAlignment = VerticalAlignment.Center };
            var fill = new Border { CornerRadius = new CornerRadius(2), Background = Line1, Height = 3, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            Anim(track, FrameworkElement.HeightProperty, d, (0, 3), (0.24, 3), (0.28, 7), (0.66, 7), (0.7, 3), (1, 3));
            Anim(fill, FrameworkElement.HeightProperty, d, (0, 3), (0.24, 3), (0.28, 7), (0.66, 7), (0.7, 3), (1, 3));
            Anim(fill, FrameworkElement.WidthProperty, d, (0, 50), (0.28, 50), (0.4, 40), (0.64, 150), (0.9, 156), (0.96, 50), (1, 50));
            progress.Children.Add(track);
            progress.Children.Add(fill);
            info.Children.Add(progress);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var shuffle = Glyphy("IcoShuffle", 13, Line2, false);
            var repeat = Glyphy("IcoRepeat", 13, Line2, false);
            var shuffleOn = Glyphy("IcoShuffle", 13, accent, false);
            var repeatOn = Glyphy("IcoRepeat", 13, accent, false);
            Anim(shuffleOn, UIElement.OpacityProperty, d, (0, 0), (0.76, 0), (0.78, 1), (0.95, 1), (0.97, 0), (1, 0));
            Anim(repeatOn, UIElement.OpacityProperty, d, (0, 0), (0.84, 0), (0.86, 1), (0.95, 1), (0.97, 0), (1, 0));
            controls.Children.Add(new Grid { Children = { shuffle, shuffleOn } });
            controls.Children.Add(Spaced(Glyphy("IcoPrev", 11, Line2, true), 0, 16));
            controls.Children.Add(Spaced(Glyphy("IcoPause", 15, Line1, true), 0, 16));
            controls.Children.Add(Spaced(Glyphy("IcoNext", 11, Line2, true), 0, 16));
            controls.Children.Add(Spaced(new Grid { Children = { repeat, repeatOn } }, 0, 16));
            info.Children.Add(controls);
            Grid.SetColumn(info, 1);
            g.Children.Add(info);
            island.Child = g;
            stage.Children.Add(island);
            // Cursor: vai até a barra, segura, arrasta para a direita, solta e toca nos botões
            stage.Children.Add(Cursor(d, (0, 330, 190), (0.22, 220, 118), (0.28, 220, 118), (0.4, 210, 118), (0.64, 320, 118), (0.7, 320, 118), (0.76, 208, 150), (0.84, 316, 150), (1, 330, 190)));
            return stage;
        }

        /// <summary>Copiados: a ilha cresce a cada item novo e encolhe ao apagar um.</summary>
        private static FrameworkElement Dynamic(Brush accent)
        {
            const double d = 5;
            var stage = new Grid();
            var island = Island(300, 70, top: 0);
            island.VerticalAlignment = VerticalAlignment.Top;
            island.CornerRadius = new CornerRadius(0, 0, 20, 20);
            Anim(island, FrameworkElement.HeightProperty, d, (0, 78), (0.12, 78), (0.2, 118), (0.36, 118), (0.44, 158), (0.62, 158), (0.7, 118), (0.9, 118), (0.96, 78), (1, 78));
            var col = new StackPanel { Margin = new Thickness(16, 12, 16, 0) };
            string[] items = { L.T("Rua das Flores, 120"), "ilha.app/novidades", L.T("Reunião às 15h") };
            double[] appear = { 0, 0.14, 0.38 };
            for (int i = 0; i < items.Length; i++)
            {
                var row = new Border { CornerRadius = new CornerRadius(9), Background = Card, Padding = new Thickness(11, 8, 11, 8), Margin = new Thickness(0, 0, 0, 6), Height = 34 };
                var dock = new DockPanel();
                var x = Glyphy("IcoClose", 9, Line2, false, 2.6);
                DockPanel.SetDock(x, Dock.Right);
                dock.Children.Add(x);
                dock.Children.Add(Txt(items[i], 11.5, Line1, false));
                row.Child = dock;
                if (i == 1)
                {
                    // O do meio ganha o "x" e é apagado
                    Anim(x, UIElement.OpacityProperty, d, (0, 0), (0.54, 0), (0.56, 1), (0.62, 1), (0.64, 0), (1, 0));
                    Anim(row, UIElement.OpacityProperty, d, (0, 0), (appear[i], 0), (appear[i] + 0.06, 1), (0.62, 1), (0.66, 0), (1, 0));
                    Anim(row, FrameworkElement.HeightProperty, d, (0, 34), (0.64, 34), (0.7, 0), (0.96, 0), (1, 34));
                }
                else
                {
                    x.Opacity = 0;
                    if (i > 0)
                        Anim(row, UIElement.OpacityProperty, d, (0, 0), (appear[i], 0), (appear[i] + 0.06, 1), (0.9, 1), (0.95, 0), (1, 0));
                }
                col.Children.Add(row);
            }
            island.Child = col;
            stage.Children.Add(island);
            stage.Children.Add(Cursor(d, (0, 340, 200), (0.48, 352, 72), (0.6, 352, 72), (0.8, 340, 200), (1, 340, 200)));
            return stage;
        }

        /// <summary>A ilha cresce e diz bom dia pelo nome, com a carinha.</summary>
        private static FrameworkElement Hello()
        {
            const double d = 4.6;
            var stage = new Grid();
            var island = Island(130, 30, top: 0);
            island.CornerRadius = new CornerRadius(0, 0, 18, 18);
            Anim(island, FrameworkElement.WidthProperty, d, (0, 130), (0.12, 130), (0.26, 300), (0.82, 300), (0.94, 130), (1, 130));
            Anim(island, FrameworkElement.HeightProperty, d, (0, 30), (0.12, 30), (0.26, 64), (0.82, 64), (0.94, 30), (1, 30));
            var name = UserName;
            var row = Nudge("hello", L.F("Bom dia, {0}!", name), L.T("Que o seu dia seja leve."));
            Anim(row, UIElement.OpacityProperty, d, (0, 0), (0.24, 0), (0.32, 1), (0.78, 1), (0.84, 0), (1, 0));
            island.Child = row;
            stage.Children.Add(island);
            stage.Children.Add(Cursor(d, (0, 300, 170), (0.1, 222, 14), (0.85, 222, 30), (1, 300, 170)));
            return stage;
        }

        /// <summary>Lembretes passando: água, esticar as pernas e hora de dormir.</summary>
        private static FrameworkElement Wellness()
        {
            const double d = 7.2;
            var stage = new Grid();
            var island = Island(340, 64, top: 0);
            island.CornerRadius = new CornerRadius(0, 0, 20, 20);
            var host = new Grid();
            var items = new (string Kind, string Title, string Text)[]
            {
                ("water", L.T("Já tomou água hoje?"), L.T("Um copo agora faz bem.")),
                ("stretch", L.T("Que tal esticar um pouco as pernas?"), L.T("Levante e dê uma volta rapidinho.")),
                ("late", L.T("Já não está tarde?"), L.T("Será que não é hora de dormir?")),
            };
            for (int i = 0; i < items.Length; i++)
            {
                var row = Nudge(items[i].Kind, items[i].Title, items[i].Text);
                double a = i / 3.0, b = (i + 1) / 3.0;
                if (i == 0) Anim(row, UIElement.OpacityProperty, d, (0, 1), (b - 0.03, 1), (b, 0), (0.97, 0), (1, 1));
                else Anim(row, UIElement.OpacityProperty, d, (0, 0), (a, 0), (a + 0.04, 1), (b - 0.03, 1), (b, 0), (1, 0));
                host.Children.Add(row);
            }
            island.Child = host;
            stage.Children.Add(island);
            // Relógio do dia passando embaixo
            var times = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 34) };
            string[] labels = { "11:00", "15:30", "01:20" };
            for (int i = 0; i < 3; i++)
            {
                var t = Txt(labels[i], 22, Line2, true, center: true);
                t.VerticalAlignment = VerticalAlignment.Bottom;
                double a = i / 3.0, b = (i + 1) / 3.0;
                if (i == 0) Anim(t, UIElement.OpacityProperty, d, (0, 1), (b - 0.03, 1), (b, 0), (0.97, 0), (1, 1));
                else Anim(t, UIElement.OpacityProperty, d, (0, 0), (a, 0), (a + 0.04, 1), (b - 0.03, 1), (b, 0), (1, 0));
                times.Children.Add(t);
            }
            stage.Children.Add(times);
            return stage;
        }

        private static FrameworkElement Nudge(string kind, string title, string text)
        {
            var grid = new Grid { Margin = new Thickness(14, 0, 14, 0), Height = 64, VerticalAlignment = VerticalAlignment.Top };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new Border { Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0), Child = NudgeArt.Build(kind), VerticalAlignment = VerticalAlignment.Center });
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            col.Children.Add(Txt(title, 13, Line1, true));
            var sub = Txt(text, 11, Line2, false);
            sub.Margin = new Thickness(0, 2, 0, 0);
            col.Children.Add(sub);
            Grid.SetColumn(col, 1);
            grid.Children.Add(col);
            return grid;
        }
        /// <summary>O ícone novo, flutuando com um brilho.</summary>
        private static FrameworkElement Icon(Brush accent)
        {
            const double d = 4;
            var stage = new Grid();
            var halo = new Ellipse { Width = 210, Height = 210, Fill = new RadialGradientBrush(Color.FromArgb(0x55, 0x7C, 0xE3, 0xA1), Colors.Transparent) };
            Anim(halo, UIElement.OpacityProperty, d, (0, 0.5), (0.5, 1), (1, 0.5));
            stage.Children.Add(halo);
            var img = new Image { Width = 132, Height = 132 };
            try { img.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icon.png")); } catch { }
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var lift = new TranslateTransform();
            img.RenderTransform = lift;
            Anim(lift, TranslateTransform.YProperty, d, (0, 4), (0.5, -4), (1, 4));
            stage.Children.Add(img);
            return stage;
        }

        // ───────────── Peças ─────────────

        /// <summary>Inicial do dia da semana (no idioma da Ilha) de um dia de novembro de 2026.</summary>
        private static string Week(int day)
        {
            var c = System.Globalization.CultureInfo.CurrentCulture;
            return c.DateTimeFormat.GetAbbreviatedDayName(new DateTime(2026, 11, day).DayOfWeek).TrimEnd('.').ToUpper(c);
        }

        private static Border Island(double w, double h, double top) => new()
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(20), Background = IslandFill, BorderBrush = IslandRim,
            BorderThickness = new Thickness(1), ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, top, 0, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.45, Direction = 270 },
        };

        private static FrameworkElement Block(UIElement content) => new Border
        {
            Width = 66, Height = 80, CornerRadius = new CornerRadius(12), Background = Card, Margin = new Thickness(5, 0, 5, 0),
            Child = new Grid { Children = { content }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };

        private static FrameworkElement Page(string title, Color color) => new Border
        {
            Width = 160, Height = 46, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, color.R, color.G, color.B)), BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }, Txt(title, 11.5, Line1, true) } },
                    Spaced(Bar(90, 4, Line3), 7),
                },
            },
        };

        private static FrameworkElement CheckRow(Brush accent, bool done) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2.5), Background = done ? accent : Brushes.Transparent, BorderBrush = done ? accent : Line2, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 5, 0) },
                Bar(done ? 26 : 32, 4, done ? Line3 : Line2),
            },
        };

        private static FrameworkElement Bars(Brush accent, HorizontalAlignment align)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
            double[] phase = { 0, 0.3, 0.15, 0.45 };
            foreach (var p in phase)
            {
                var b = new Border { Width = 2.5, Height = 10, CornerRadius = new CornerRadius(1), Background = accent, Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Center };
                Anim(b, FrameworkElement.HeightProperty, 0.9, (0, 4 + p * 8), (0.5, 12 - p * 8), (1, 4 + p * 8));
                row.Children.Add(b);
            }
            return row;
        }

        /// <summary>Ponteiro do mouse andando pelo palco (x, y em pixels do palco).</summary>
        private static FrameworkElement Cursor(double d, params (double T, double X, double Y)[] path)
        {
            var arrow = new Path
            {
                Data = Geometry.Parse("M0 0 L0 15 L4 11.4 L6.8 17.6 L9.4 16.4 L6.7 10.4 L11.6 10.4 Z"),
                Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.5 },
            };
            var move = new TranslateTransform();
            arrow.RenderTransform = move;
            var xs = new (double, double)[path.Length];
            var ys = new (double, double)[path.Length];
            for (int i = 0; i < path.Length; i++) { xs[i] = (path[i].T, path[i].X); ys[i] = (path[i].T, path[i].Y); }
            Anim(move, TranslateTransform.XProperty, d, xs);
            Anim(move, TranslateTransform.YProperty, d, ys);
            return new Canvas { IsHitTestVisible = false, Children = { arrow } };
        }

        private static FrameworkElement Bar(double w, double h, Brush fill) =>
            new Border { Width = w, Height = h, CornerRadius = new CornerRadius(h / 2), Background = fill, HorizontalAlignment = HorizontalAlignment.Left };

        private static T Spaced<T>(T e, double top, double left = 0) where T : FrameworkElement
        {
            e.Margin = new Thickness(left, top, 0, 0);
            return e;
        }

        private static TextBlock Txt(string s, double size, Brush fg, bool bold, bool center = false) => new()
        {
            Text = s, FontSize = size, Foreground = fg, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = center ? VerticalAlignment.Center : VerticalAlignment.Top,
        };

        private static FrameworkElement Glyphy(string key, double size, Brush fg, bool filled, double thickness = 2) => new Glyph
        {
            Data = (Geometry)Application.Current.FindResource(key), Size = size, Foreground = fg, Filled = filled, Thickness = filled ? 0 : thickness,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };

        /// <summary>Uma "capa de álbum" desenhada: degradê quente com um círculo.</summary>
        private static Brush ArtBrush()
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new LinearGradientBrush(Color.FromRgb(0xFF, 0x7A, 0x59), Color.FromRgb(0x8E, 0x3C, 0xF7), 45), null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xE0, 0x8A)), null, new EllipseGeometry(new Point(64, 40), 24, 24)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x40, 0x10, 0x10, 0x30)), null, new RectangleGeometry(new Rect(0, 70, 100, 30))));
            return new DrawingBrush(g) { Stretch = Stretch.Fill };
        }

        private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));
        private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

        /// <summary>Animação em loop por quadros-chave (tempo de 0 a 1 do ciclo, valor), com entrada e saída suaves.</summary>
        private static void Anim(IAnimatable target, DependencyProperty property, double seconds, params (double T, double V)[] keys)
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever };
            foreach (var (t, v) in keys)
                a.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromPercent(Math.Clamp(t, 0, 1)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
            target.BeginAnimation(property, a);
        }
    }
}
