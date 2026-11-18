using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace Ilha
{
    /// <summary>
    /// Apresentação, no espírito do Assistente de Configuração do Mac: uma pergunta por tela, texto
    /// grande e centralizado, e uma ilha de verdade lá em cima que mostra na hora cada escolha.
    /// Passos: boas-vindas → nome → do seu jeito (idioma, tema, posição) → recursos → pronto.
    /// Com <c>nameOnly</c>, só pede o nome (para quem atualizou e ainda não tem).
    /// </summary>
    public sealed class SetupWindow : Window
    {
        private enum Step { Welcome, Name, Look, Features, Ready }

        private readonly AppSettings _s;
        private readonly bool _replay, _nameOnly;
        private readonly Step[] _steps;
        private int _index;
        private bool _done;

        private readonly Grid _screen = new();
        private readonly Border _island = new();
        private readonly Grid _islandContent = new();
        private readonly ContentControl _body = new() { Focusable = false };
        private readonly Button _next;
        private readonly Button _back;
        private readonly StackPanel _progress = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        private TextBox? _nameBox;

        // A janela cobre a tela inteira (transparente); a apresentação é um painel preto no centro,
        // que no final vira a ilha e sobe até o topo.
        private const double PanelW = 680, PanelH = 640, PanelRadius = 26;
        private readonly Grid _stage = new();
        private readonly Border _dim = new() { Background = Brushes.Black, Opacity = 0 };
        private readonly Border _panel = new();
        private readonly Grid _wizard = new();
        private readonly Grid _hello = new();
        private readonly Grid _helloWords = new();
        private readonly System.Windows.Threading.DispatcherTimer _helloTimer = new() { Interval = TimeSpan.FromSeconds(1.9) };
        private int _helloIndex;
        private bool _helloShowing;
        private readonly TranslateTransform _panelMove = new();
        private readonly ScaleTransform _flightScale = new();
        private readonly ScaleTransform _impactScale = new();
        private double _attach; // 0 = quatro cantos redondos; 1 = colada no topo (cantos de cima retos)
        private bool _morphing;
        private bool _closed;

        /// <summary>"Olá" em vários idiomas, como na apresentação de um Mac novo.</summary>
        private static readonly string[] Hellos =
        {
            "Hello", "Olá", "Hola", "Bonjour", "こんにちは", "Ciao", "안녕하세요", "Hallo",
            "你好", "Привет", "नमस्ते", "Merhaba", "Γεια σου", "Hej", "Salut", "Ahoj",
        };

        public event Action? Finished;

        public SetupWindow(AppSettings settings, bool replay = false, bool nameOnly = false)
        {
            _s = settings;
            _replay = replay;
            _nameOnly = nameOnly;
            _steps = nameOnly ? new[] { Step.Name } : new[] { Step.Welcome, Step.Name, Step.Look, Step.Features, Step.Ready };
            Title = "Ilha";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            PlaceOnTargetScreen();
            SetResourceReference(FontFamilyProperty, "UiFont");
            SetResourceReference(ForegroundProperty, "WinFg");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
            // A apresentação é sempre escura (a janela preta que vira a ilha), mesmo com o tema claro escolhido.
            ThemeMode = ThemeMode.Dark;
            UseDarkColors();
            ApplyAccent();
            SourceInitialized += (_, _) => WindowState = WindowState.Maximized;

            var root = _wizard;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Topo: um pedaço de "tela" com a ilha de verdade ──
            BuildScreen();
            root.Children.Add(_screen);

            _back = TextButton(L.T("Voltar"));
            _back.HorizontalAlignment = HorizontalAlignment.Left;
            _back.VerticalAlignment = VerticalAlignment.Top;
            _back.Margin = new Thickness(18, 12, 0, 0);
            WindowChrome.SetIsHitTestVisibleInChrome(_back, true);
            _back.Click += (_, _) => Go(_index - 1);
            root.Children.Add(_back);

            if (_replay)
            {
                var close = TextButton(L.T("Fechar"));
                close.HorizontalAlignment = HorizontalAlignment.Right;
                close.VerticalAlignment = VerticalAlignment.Top;
                close.Margin = new Thickness(0, 12, 18, 0);
                WindowChrome.SetIsHitTestVisibleInChrome(close, true);
                close.Click += (_, _) => Finish();
                root.Children.Add(close);
            }

            Grid.SetRow(_body, 1);
            root.Children.Add(_body);

            // ── Rodapé: botão principal e o andamento ──
            var footer = new StackPanel { Margin = new Thickness(0, 0, 0, 30) };
            _next = PrimaryButton();
            _next.Click += (_, _) => Next();
            footer.Children.Add(_next);
            _progress.Margin = new Thickness(0, 18, 0, 0);
            if (_steps.Length > 1)
            {
                for (int i = 0; i < _steps.Length; i++)
                {
                    var seg = new Border { Height = 3, Width = 22, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(3, 0, 3, 0) };
                    _progress.Children.Add(seg);
                }
                footer.Children.Add(_progress);
            }
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            // ── O painel preto no centro (que vai virar a ilha) ──
            _panel.Width = PanelW;
            _panel.Height = PanelH;
            _panel.HorizontalAlignment = HorizontalAlignment.Center;
            _panel.VerticalAlignment = VerticalAlignment.Center;
            _panel.SetResourceReference(Border.BackgroundProperty, "WinBg");
            _panel.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
            _panel.BorderThickness = new Thickness(1);
            _panel.CornerRadius = new CornerRadius(PanelRadius);
            _panel.RenderTransformOrigin = new Point(0.5, 0.5);
            _panel.RenderTransform = new TransformGroup { Children = { _flightScale, _impactScale, _panelMove } };
            _panel.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 60, ShadowDepth = 18, Opacity = 0.55, Direction = 270 };
            BuildHello();
            _panel.Child = new Grid { Children = { _wizard, _hello, _teaser } };
            _panel.SizeChanged += (_, _) => UpdatePanelShape();
            _stage.Children.Add(_dim);
            _stage.Children.Add(_panel);
            Content = _stage;
            CompositionTarget.Rendering += OnRendering;
            Closed += (_, _) => { _closed = true; CompositionTarget.Rendering -= OnRendering; };

            PreviewKeyDown += (_, e) =>
            {
                if (_morphing) { e.Handled = true; return; }
                if (_helloShowing && (e.Key == Key.Enter || e.Key == Key.Space)) { LeaveHello(); e.Handled = true; }
                else if (e.Key == Key.Enter && _next.IsEnabled) { Next(); e.Handled = true; }
                else if (e.Key == Key.Escape && _replay) { Finish(); e.Handled = true; }
            };
            Closing += (_, e) =>
            {
                if (_done) return;
                // Sem nome não segue; com nome, fechar é o mesmo que terminar.
                if (string.IsNullOrWhiteSpace(_s.UserName) && !_replay)
                {
                    e.Cancel = true;
                    Go(Array.IndexOf(_steps, Step.Name));
                    return;
                }
                _done = true;
                Save();
                Dispatcher.BeginInvoke(() => Finished?.Invoke());
            };
            Loaded += (_, _) =>
            {
                int first = 0;
#if DEBUG
                if (int.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_STEP"), out int t)) first = Math.Clamp(t, 0, _steps.Length - 1);
                if (Environment.GetEnvironmentVariable("ILHA_TEST_NAME") is { Length: > 0 } n) _s.UserName = n;
#endif
                Go(first, animate: false);
                Activate();
                // A apresentação nasce da própria ilha, lá no topo (ver Emerge).
                Emerge(showHello: !_nameOnly && first == 0);
#if DEBUG
                if (double.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_FINALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double finaleAt))
                {
                    var auto = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(finaleAt <= 1 ? 1.5 : finaleAt) };
                    auto.Tick += (_, _) => { auto.Stop(); LeaveHello(); Finish(); };
                    auto.Start();
                }
                // Vitrine: a ilha abre e desce (~5 s), o "Olá" até 14 s, depois cada etapa por 2,8 s e o final.
                if (Environment.GetEnvironmentVariable("ILHA_TEST_TOUR") == "1")
                {
                    var tour = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(14) };
                    tour.Tick += (_, _) =>
                    {
                        tour.Interval = TimeSpan.FromSeconds(2.8);
                        if (_helloShowing) { LeaveHello(); return; }
                        if (_done) { tour.Stop(); return; }
                        Next();
                    };
                    tour.Start();
                }
#endif
            };
        }

        // ───────────── Tela e cores ─────────────

        /// <summary>A janela cobre o monitor onde a ilha vai ficar (maximizada, transparente).</summary>
        private void PlaceOnTargetScreen()
        {
            var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _s.MonitorDevice)
                         ?? System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];
            double k = 1;
            try
            {
                using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                k = g.DpiX / 96.0;
            }
            catch { }
            var b = screen.Bounds;
            Left = b.Left / k + 40;
            Top = b.Top / k + 40;
            Width = 400;
            Height = 300;
        }

        private void UseDarkColors()
        {
            static SolidColorBrush B(int rgb) { var x = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)); x.Freeze(); return x; }
            Resources["WinBg"] = B(0x0A0A0B);
            Resources["WinFg"] = B(0xF5F5F7);
            Resources["CardBg"] = B(0x1A1A1E);
            Resources["Line"] = B(0x2A2A30);
            Resources["Text2"] = B(0xA1A1A8);
            Resources["Text3"] = B(0x6E6E76);
            Resources["Control"] = B(0x26262B);
        }

        /// <summary>Cantos do painel: redondos, proporcionais à altura; colado no topo, os de cima ficam retos.</summary>
        private void UpdatePanelShape()
        {
            double h = _panel.ActualHeight, w = _panel.ActualWidth;
            if (h <= 0 || w <= 0) return;
            double r = Math.Min(PanelRadius, h * 0.4);
            double top = r * (1 - _attach);
            _panel.CornerRadius = new CornerRadius(top, top, r, r);
            // O conteúdo acompanha a curva (o topo da apresentação tem um "papel de parede").
            var clip = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(top, 0), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(w - top, 0), true));
            fig.Segments.Add(new ArcSegment(new Point(w, top), new Size(top, top), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(new Point(w, h - r), true));
            fig.Segments.Add(new ArcSegment(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(new Point(r, h), true));
            fig.Segments.Add(new ArcSegment(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(new Point(0, top), true));
            fig.Segments.Add(new ArcSegment(new Point(top, 0), new Size(top, top), 0, false, SweepDirection.Clockwise, true));
            clip.Figures.Add(fig);
            if (_panel.Child is UIElement child) child.Clip = clip;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (_morphing) UpdatePanelShape();
        }

        // ───────────── Começo: a ilha abre, mostra para que serve e vira a janela ─────────────

        private readonly Grid _teaser = new() { IsHitTestVisible = false };

        /// <summary>Tamanho e posição da ilha recolhida (como a ilha de verdade fica no topo).</summary>
        private (double W, double H, double Top, bool Attached) IslandTarget()
        {
            double scale = Math.Clamp(_s.Scale, 0.6, 1.6);
            double w = Math.Max(60, _s.CollapsedWidth * scale), h = Math.Max(20, _s.CollapsedHeight * scale);
            bool attached = string.Equals(_s.IslandStyle, "Attached", StringComparison.OrdinalIgnoreCase);
            double top = attached ? 0 : 10 * scale;
#if DEBUG
            if (int.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_Y"), out int testY)) top += testY;
#endif
            return (w, h, top, attached);
        }

        private void Emerge(bool showHello)
        {
            _hello.Visibility = showHello ? Visibility.Visible : Visibility.Collapsed;
            if (!SystemParameters.ClientAreaAnimation)
            {
                _dim.Opacity = 0.42;
                _teaser.Visibility = Visibility.Collapsed;
                if (showHello) ShowHello();
                return;
            }
            _panel.Opacity = 0; // escondido até a ilha estar posicionada no topo
            // Espera o layout (precisa da altura da tela).
            Dispatcher.BeginInvoke(() => RunEmerge(showHello), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 1. A ilha desce da borda da tela. 2. Abre, como quando você passa o mouse, e mostra
        /// em sequência a música, um compromisso e o timer. 3. Se solta do topo, desce até o meio
        /// da tela crescendo e vira a janela da apresentação, onde aparece o "Olá".
        /// </summary>
        private void RunEmerge(bool showHello)
        {
            _morphing = true;
            var (w, h, top, attached) = IslandTarget();
            double sh = _stage.ActualHeight;
            double Y(double height) => top + height / 2 - sh / 2; // mantém a borda de cima colada no topo
            TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);
            const double OpenW = 340, OpenH = 92;

            var islandColor = IslandColor();
            var ink = IslandLight ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White;
            var panelColor = Color.FromRgb(0x0A, 0x0A, 0x0B);
            _wizard.Opacity = 0;
            _hello.Opacity = 0;
            _attach = attached ? 1 : 0;
            _panel.Background = new SolidColorBrush(islandColor);
            _panel.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0xFF, 0xFF, 0xFF));
            var shadow = (System.Windows.Media.Effects.DropShadowEffect)_panel.Effect;
            shadow.Opacity = 0;
            BuildTeaser(ink);

            // 1. Desce da borda.
            var drop = new SpringEase { Damping = 0.8, Frequency = 10 };
            _panel.Width = w;
            _panel.Height = 0;
            _panelMove.Y = Y(0);
            _panel.Opacity = 1;
            _panel.BeginAnimation(HeightProperty, new DoubleAnimation(0, h, Ms(480)) { BeginTime = Ms(150), EasingFunction = drop });
            _panelMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Y(0), Y(h), Ms(480)) { BeginTime = Ms(150), EasingFunction = drop });

            // 2. Abre (como no passar do mouse) e mostra o que faz.
            var open = new SpringEase { Damping = 0.76, Frequency = 8.5 };
            double tOpen = 900;
            Delay(tOpen, () =>
            {
                _panel.BeginAnimation(WidthProperty, new DoubleAnimation(w, OpenW, Ms(650)) { EasingFunction = open });
                _panel.BeginAnimation(HeightProperty, new DoubleAnimation(h, OpenH, Ms(650)) { EasingFunction = open });
                _panelMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Y(h), Y(OpenH), Ms(650)) { EasingFunction = open });
            });
            double each = 1000;
            int count = _teaser.Children.Count;
            for (int i = 0; i < count; i++)
            {
                var item = (FrameworkElement)_teaser.Children[i];
                var move = (TranslateTransform)item.RenderTransform;
                double inAt = tOpen + 250 + i * each;
                var fade = new DoubleAnimationUsingKeyFrames { BeginTime = Ms(inAt), Duration = Ms(each) };
                fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(Ms(300)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(Ms(each - 200))));
                fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(Ms(each)), new CubicEase { EasingMode = EasingMode.EaseIn }));
                item.BeginAnimation(OpacityProperty, fade);
                var slide = new DoubleAnimationUsingKeyFrames { BeginTime = Ms(inAt), Duration = Ms(each) };
                slide.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(Ms(380)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                slide.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(Ms(each - 200))));
                slide.KeyFrames.Add(new EasingDoubleKeyFrame(-8, KeyTime.FromTimeSpan(Ms(each)), new CubicEase { EasingMode = EasingMode.EaseIn }));
                move.BeginAnimation(TranslateTransform.YProperty, slide);
            }

            // 3. Solta do topo e desce até o meio, crescendo até virar a janela.
            double tFree = tOpen + 250 + count * each;
            Delay(tFree, () =>
            {
                AnimateAttach(_attach, 0, 200);
                _panelMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Y(OpenH), Y(OpenH) - 6, Ms(180)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            });
            double tDrop = tFree + 180;
            Delay(tDrop, () =>
            {
                var grow = new SpringEase { Damping = 0.8, Frequency = 6.5 };
                _panelMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Y(OpenH) - 6, 0, Ms(1000)) { EasingFunction = grow });
                _panel.BeginAnimation(WidthProperty, new DoubleAnimation(OpenW, PanelW, Ms(1000)) { EasingFunction = grow });
                _panel.BeginAnimation(HeightProperty, new DoubleAnimation(OpenH, PanelH, Ms(1000)) { EasingFunction = grow });
                _panel.Background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(panelColor, Ms(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                _panel.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), Ms(500)) { BeginTime = Ms(300) });
                shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(0, 0.55, Ms(800)));
                _dim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.42, Ms(900)));
            });

            // 4. O "Olá" (ou a primeira etapa) aparece dentro da janela.
            Delay(tDrop + 550, () =>
            {
                _teaser.Visibility = Visibility.Collapsed;
                UIElement page = showHello ? _hello : _wizard;
                page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                if (showHello) ShowHello();
            });
            Delay(tDrop + 1100, () =>
            {
                _morphing = false;
                _panel.SetResourceReference(Border.BackgroundProperty, "WinBg");
                _panel.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
                UpdatePanelShape();
            });
        }

        private void Delay(double ms, Action action)
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
            t.Tick += (_, _) => { t.Stop(); action(); };
            t.Start();
        }

        /// <summary>Cantos de cima: retos (colada no topo) ↔ redondos (solta).</summary>
        private void AnimateAttach(double from, double to, double ms)
        {
            var start = DateTime.Now;
            EventHandler? step = null;
            step = (_, _) =>
            {
                double k = Math.Min(1, (DateTime.Now - start).TotalMilliseconds / ms);
                _attach = from + (to - from) * k;
                UpdatePanelShape();
                if (k >= 1) CompositionTarget.Rendering -= step;
            };
            CompositionTarget.Rendering += step;
        }

        /// <summary>O que a ilha mostra ao abrir: a música, um compromisso de hoje e o timer.</summary>
        private void BuildTeaser(Color ink)
        {
            _teaser.Children.Clear();
            _teaser.Visibility = Visibility.Visible;
            var dim = new SolidColorBrush(Color.FromArgb(0x99, ink.R, ink.G, ink.B));
            var fg = new SolidColorBrush(ink);
            var accent = ((SolidColorBrush)Resources["Accent"]).Color;

            FrameworkElement Item(UIElement art, string title, string sub, UIElement? right = null)
            {
                var g = new Grid { Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0, RenderTransform = new TranslateTransform(0, 8) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new Border { Width = 46, Height = 46, Margin = new Thickness(0, 0, 14, 0), Child = art });
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                col.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = fg });
                col.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = dim, Margin = new Thickness(0, 2, 0, 0) });
                Grid.SetColumn(col, 1);
                g.Children.Add(col);
                if (right != null) { Grid.SetColumn(right, 2); g.Children.Add(right); }
                return g;
            }

            // Música: capa, título e o equalizador mexendo.
            var cover = new Border { CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(Color.FromRgb(0xFF, 0x7A, 0x59), Color.FromRgb(0x8E, 0x3C, 0xF7), 45) };
            var bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var sp in new[] { 0.42, 0.31, 0.5, 0.37 })
            {
                var sc = new ScaleTransform(1, 0.4);
                var bar = new Border { Width = 3, Height = 16, Margin = new Thickness(1.5, 0, 1.5, 0), CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(accent), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = sc };
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromSeconds(sp)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                bars.Children.Add(bar);
            }
            _teaser.Children.Add(Item(cover, "Maré Baixa", "Costa Verde", bars));

            // Agenda: o dia de hoje e um compromisso.
            var today = DateTime.Today;
            var culture = System.Globalization.CultureInfo.GetCultureInfo(L.English ? "en-US" : "pt-BR");
            var dayTile = new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x18, ink.R, ink.G, ink.B)) };
            var dayCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            dayCol.Children.Add(new TextBlock { Text = today.ToString("MMM", culture).TrimEnd('.').ToUpperInvariant(), FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Center });
            dayCol.Children.Add(new TextBlock { Text = today.Day.ToString(), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) });
            dayTile.Child = dayCol;
            _teaser.Children.Add(Item(dayTile, L.T("Reunião às 15h"), L.T("Hoje, na sua agenda")));

            // Timer: o contorno (parte já cheia) e o tempo.
            var ring = new Grid();
            ring.Children.Add(new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2.5), BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, ink.R, ink.G, ink.B)) });
            var arc = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2.5), BorderBrush = new SolidColorBrush(accent) };
            arc.OpacityMask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black, 0.62), new GradientStop(Colors.Transparent, 0.62) } };
            ring.Children.Add(arc);
            ring.Children.Add(new TextBlock { Text = "25", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            _teaser.Children.Add(Item(ring, "24:59", L.T("Foco, sem distrações")));
        }

        // ───────────── Olá ─────────────

        /// <summary>Nome de cada idioma, mostrado bem pequeno embaixo da palavra.</summary>
        private static readonly string[] HelloLangs =
        {
            "English", "Português", "Español", "Français", "日本語", "Italiano", "한국어", "Deutsch",
            "中文", "Русский", "हिन्दी", "Türkçe", "Ελληνικά", "Svenska", "Română", "Čeština",
        };

        /// <summary>Luz de fundo: um tom suave diferente para cada idioma.</summary>
        private static readonly Color[] HelloTints =
        {
            Color.FromRgb(0x8E, 0xB8, 0xFF), Color.FromRgb(0x7C, 0xE3, 0xA1), Color.FromRgb(0xFF, 0xB0, 0x8A),
            Color.FromRgb(0xC9, 0xA7, 0xFF), Color.FromRgb(0xFF, 0x9F, 0xB8), Color.FromRgb(0x8F, 0xE0, 0xE8),
            Color.FromRgb(0xFF, 0xD2, 0x7A), Color.FromRgb(0xA9, 0xC8, 0xFF),
        };

        private readonly TextBlock _helloLang = new();
        private readonly RadialGradientBrush _helloGlow = new();

        private void BuildHello()
        {
            _hello.SetResourceReference(Panel.BackgroundProperty, "WinBg");
            _hello.ClipToBounds = true;

            // Luz bem suave atrás da palavra; muda de cor a cada idioma e respira devagar.
            _helloGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, 0x8E, 0xB8, 0xFF), 0));
            _helloGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0x8E, 0xB8, 0xFF), 0.45));
            _helloGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x8E, 0xB8, 0xFF), 1));
            var glow = new Border
            {
                Width = 640, Height = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 70), IsHitTestVisible = false, Background = _helloGlow,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
            };
            if (SystemParameters.ClientAreaAnimation)
            {
                var breathe = new DoubleAnimation(0.9, 1.12, TimeSpan.FromSeconds(3.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                ((ScaleTransform)glow.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
                ((ScaleTransform)glow.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
            }
            _hello.Children.Add(glow);

            _helloWords.Height = 170;
            _helloWords.VerticalAlignment = VerticalAlignment.Center;
            _helloWords.Margin = new Thickness(0, 0, 0, 80);
            _helloWords.IsHitTestVisible = false;
            _hello.Children.Add(_helloWords);

            _helloLang.FontSize = 12;
            _helloLang.FontWeight = FontWeights.Medium;
            _helloLang.HorizontalAlignment = HorizontalAlignment.Center;
            _helloLang.VerticalAlignment = VerticalAlignment.Center;
            _helloLang.Margin = new Thickness(0, 150, 0, 0);
            _helloLang.Opacity = 0;
            _helloLang.IsHitTestVisible = false;
            _helloLang.SetResourceReference(TextBlock.ForegroundProperty, "Text3");
            TextOptions.SetTextFormattingMode(_helloLang, TextFormattingMode.Ideal);
            _hello.Children.Add(_helloLang);

            var start = PrimaryButton();
            start.Content = L.T("Começar");
            start.Click += (_, _) => LeaveHello();
            var hint = new TextBlock { Text = L.T("ou pressione Enter"), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Text3");
            var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 44), Opacity = 0, Children = { start, hint } };
            bottom.Tag = "start";
            _hello.Children.Add(bottom);
            _hello.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not Button) LeaveHello(); };
            _helloTimer.Interval = TimeSpan.FromSeconds(2.4);
            _helloTimer.Tick += (_, _) => NextHello();
        }

        private void ShowHello()
        {
            _helloShowing = true;
            _hello.Visibility = Visibility.Visible;
            _hello.Opacity = 1;
            _helloIndex = -1;
            NextHello();
            _helloTimer.Start();
            foreach (var child in _hello.Children)
                if (child is FrameworkElement { Tag: "start" } bottom)
                    bottom.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 700 : 0))
                    {
                        BeginTime = TimeSpan.FromSeconds(SystemParameters.ClientAreaAnimation ? 2.2 : 0),
                    });
        }

        /// <summary>
        /// A palavra é "escrita à mão" da esquerda para a direita: o contorno das letras corre
        /// como uma caneta, logo atrás vem o preenchimento, e uma faixa macia revela tudo no ritmo
        /// da escrita. Na troca, a palavra anterior se desfaz (desfoca, sobe e some).
        /// </summary>
        private void NextHello()
        {
            _helloIndex = (_helloIndex + 1) % Hellos.Length;
            bool motion = SystemParameters.ClientAreaAnimation;
            TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);
            var outEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            foreach (UIElement old in _helloWords.Children.Cast<UIElement>().ToList())
            {
                if (old is not FrameworkElement f || f.Tag as string == "leaving") continue;
                f.Tag = "leaving";
                if (!motion) { _helloWords.Children.Remove(f); continue; }
                var fade = new DoubleAnimation(0, Ms(460)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                fade.Completed += (_, _) => _helloWords.Children.Remove(f);
                f.BeginAnimation(OpacityProperty, fade);
                var blur = new System.Windows.Media.Effects.BlurEffect { Radius = 0 };
                f.Effect = blur;
                blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, new DoubleAnimation(14, Ms(460)));
                if (f.RenderTransform is TranslateTransform tt)
                    tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-16, Ms(460)) { EasingFunction = outEase });
            }

            // Desenho da palavra: letra cursiva para o alfabeto latino/grego/cirílico; as outras
            // escritas caem para a fonte do sistema de cada uma.
            string text = Hellos[_helloIndex];
            bool latin = text.All(c => c < 0x0530);
            var family = new FontFamily(latin ? "Segoe Script" : "Yu Gothic UI, Malgun Gothic, Microsoft YaHei UI, Nirmala UI, Segoe UI");
            var face = new Typeface(family, FontStyles.Normal, latin ? FontWeights.Normal : FontWeights.Light, FontStretches.Normal);
            var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                face, latin ? 92 : 80, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var geometry = formatted.BuildGeometry(new Point(0, 0));
            var bounds = geometry.Bounds;
            geometry.Transform = new TranslateTransform(-bounds.X, -bounds.Y);
            var shape = geometry.GetFlattenedPathGeometry();
            shape.Freeze();
            double maxW = PanelW - 80;
            double fit = bounds.Width > maxW ? maxW / bounds.Width : 1;

            var ink = (Brush)FindResource("WinFg");
            var fill = new System.Windows.Shapes.Path { Data = shape, Fill = ink, Opacity = motion ? 0 : 1 };
            var pen = new System.Windows.Shapes.Path
            {
                Data = shape, Stroke = ink, StrokeThickness = 1.3,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Opacity = motion ? 1 : 0,
            };
            var word = new Grid
            {
                Width = bounds.Width, Height = bounds.Height,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                LayoutTransform = new ScaleTransform(fit, fit),
                RenderTransform = new TranslateTransform(),
                Children = { fill, pen },
            };
            _helloWords.Children.Add(word);

            // Idioma e cor da luz.
            _helloLang.Text = HelloLangs[_helloIndex];
            var tint = HelloTints[_helloIndex % HelloTints.Length];
            byte[] alphas = { 0x34, 0x12, 0 };
            for (int i = 0; i < 3; i++)
            {
                var target = Color.FromArgb(alphas[i], tint.R, tint.G, tint.B);
                if (motion) _helloGlow.GradientStops[i].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(target, Ms(1400)) { EasingFunction = outEase });
                else _helloGlow.GradientStops[i].Color = target;
            }
            if (!motion) { _helloLang.Opacity = 1; return; }

            var begin = Ms(_helloIndex == 0 ? 300 : 260);
            double write = Math.Clamp(420 + text.Length * 95, 650, 1250);

            // Faixa que revela da esquerda para a direita, com a borda bem macia.
            var mask = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            var lead = new GradientStop(Colors.Black, -0.18);
            var edge = new GradientStop(Colors.Transparent, 0);
            mask.GradientStops.Add(lead);
            mask.GradientStops.Add(edge);
            word.OpacityMask = mask;
            var writeEase = new SineEase { EasingMode = EasingMode.EaseInOut };
            lead.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(-0.18, 1.0, Ms(write)) { BeginTime = begin, EasingFunction = writeEase });
            edge.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0, 1.18, Ms(write)) { BeginTime = begin, EasingFunction = writeEase });

            // A caneta: o contorno corre e depois some, deixando a letra cheia.
            double units = 220;
            pen.StrokeDashArray = new DoubleCollection { units, units };
            pen.StrokeDashOffset = units;
            pen.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, new DoubleAnimation(units, 0, Ms(write * 1.25)) { BeginTime = begin, EasingFunction = outEase });
            pen.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.0, Ms(600)) { BeginTime = begin + Ms(write * 0.9) });
            fill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(write * 0.9)) { BeginTime = begin + Ms(write * 0.25), EasingFunction = outEase });

            // Um leve flutuar para cima enquanto escreve.
            ((TranslateTransform)word.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, Ms(write + 400)) { BeginTime = begin, EasingFunction = outEase });

            // O nome do idioma troca com um fade curto.
            var langOut = new DoubleAnimation(0, Ms(180));
            langOut.Completed += (_, _) =>
                _helloLang.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.9, Ms(500)) { BeginTime = Ms(Math.Max(0, write * 0.5 - 180)), EasingFunction = outEase });
            _helloLang.BeginAnimation(OpacityProperty, langOut);
        }

        private void LeaveHello()
        {
            if (!_helloShowing) return;
            _helloShowing = false;
            _helloTimer.Stop();
            if (!SystemParameters.ClientAreaAnimation) { _hello.Visibility = Visibility.Collapsed; return; }
            var dur = TimeSpan.FromMilliseconds(460);
            var fade = new DoubleAnimation(0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => { _hello.Visibility = Visibility.Collapsed; _helloWords.Children.Clear(); };
            _hello.BeginAnimation(OpacityProperty, fade);
            // A primeira etapa entra logo atrás.
            _wizard.Opacity = 0;
            _wizard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(260) });
            Go(_index, animate: true);
        }

        // ───────────── Passos ─────────────

        private void Next()
        {
            var step = _steps[_index];
            if (step == Step.Name)
            {
                string n = _nameBox?.Text.Trim() ?? "";
                if (n.Length == 0) return;
                _s.UserName = n;
            }
            if (_index >= _steps.Length - 1) Finish();
            else Go(_index + 1);
        }

        private void Go(int index, bool animate = true)
        {
            if (index < 0 || index >= _steps.Length) return;
            int dir = Math.Sign(index - _index);
            _index = index;
            var step = _steps[index];
            var page = step switch
            {
                Step.Welcome => WelcomePage(),
                Step.Name => NamePage(),
                Step.Look => LookPage(),
                Step.Features => FeaturesPage(),
                _ => ReadyPage(),
            };
            _body.Content = page;
            if (animate && SystemParameters.ClientAreaAnimation)
            {
                var move = new TranslateTransform(24 * (dir == 0 ? 1 : dir), 0);
                page.RenderTransform = move;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
            }
            _back.Visibility = index == 0 ? Visibility.Hidden : Visibility.Visible;
            _next.Content = index == _steps.Length - 1
                ? (_nameOnly ? L.T("Pronto") : _replay ? L.T("Concluir") : L.T("Começar a usar"))
                : L.T("Continuar");
            _next.IsEnabled = step != Step.Name || (_nameBox?.Text.Trim().Length ?? 0) > 0;
            for (int i = 0; i < _progress.Children.Count; i++)
            {
                var seg = (Border)_progress.Children[i];
                seg.SetResourceReference(Border.BackgroundProperty, i <= index ? "Accent" : "Line");
            }
            UpdateIsland(step, animate);
            if (step == Step.Name) Dispatcher.BeginInvoke(() => { _nameBox?.Focus(); _nameBox?.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
        }

        private FrameworkElement Page(string title, string? subtitle, UIElement? content = null)
        {
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Width = 500, Margin = new Thickness(0, 22, 0, 0) };
            col.Children.Add(new TextBlock
            {
                Text = title, FontSize = 30, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            });
            if (subtitle != null)
            {
                var sub = new TextBlock { Text = subtitle, FontSize = 15, LineHeight = 22, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
                sub.SetResourceReference(TextBlock.ForegroundProperty, "Text2");
                col.Children.Add(sub);
            }
            if (content != null) col.Children.Add(content);
            return col;
        }

        private FrameworkElement WelcomePage() =>
            Page(L.T("Boas-vindas à Ilha"),
                 L.T("Um espaço discreto no topo da tela. Passe o mouse e ele abre com o que você precisa agora."));

        private FrameworkElement NamePage()
        {
            // Campo grande, sem moldura: só uma linha embaixo, como nos apps da Apple.
            var box = new TextBox
            {
                FontSize = 26, FontWeight = FontWeights.Medium, TextAlignment = TextAlignment.Center, MaxLength = 30,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 6, 0, 8),
                Template = (ControlTemplate)XamlReader.Parse(
                    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TextBox'>" +
                    "<ScrollViewer x:Name='PART_ContentHost' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Margin='{TemplateBinding Padding}'/></ControlTemplate>"),
                Text = _s.UserName,
            };
            box.SetResourceReference(ForegroundProperty, "WinFg");
            box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Accent");
            _nameBox = box;
            var placeholder = new TextBlock { Text = L.T("Seu nome"), FontSize = 26, FontWeight = FontWeights.Medium, TextAlignment = TextAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(0, 6, 0, 0) };
            placeholder.SetResourceReference(TextBlock.ForegroundProperty, "Text3");
            var underline = new Border { Height = 1.5, Width = 260, CornerRadius = new CornerRadius(1) };
            underline.SetResourceReference(Border.BackgroundProperty, "Line");
            var focusLine = new Border { Height = 1.5, Width = 0, CornerRadius = new CornerRadius(1) };
            focusLine.SetResourceReference(Border.BackgroundProperty, "Accent");
            var lines = new Grid { Children = { underline, focusLine } };
            void Sync()
            {
                string n = box.Text.Trim();
                placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                _next.IsEnabled = n.Length > 0;
                ShowGreeting(n);
            }
            box.TextChanged += (_, _) => Sync();
            box.GotKeyboardFocus += (_, _) => focusLine.BeginAnimation(WidthProperty, new DoubleAnimation(260, TimeSpan.FromMilliseconds(360)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            box.LostKeyboardFocus += (_, _) => focusLine.BeginAnimation(WidthProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)));
            var field = new Grid { Width = 320, Margin = new Thickness(0, 34, 0, 0), Children = { placeholder, box } };
            var col = new StackPanel { Children = { field, lines } };
            var hint = new TextBlock { Text = L.T("Pode ser um apelido. Dá para trocar depois nas configurações."), FontSize = 12.5, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Text3");
            col.Children.Add(hint);
            Dispatcher.BeginInvoke(Sync);
            return Page(L.T("Como você quer ser chamado?"),
                        L.T("A Ilha usa o seu nome para dizer bom dia, e só isso. Ele fica no seu computador."), col);
        }

        private FrameworkElement LookPage()
        {
            var col = new StackPanel { Margin = new Thickness(0, 26, 0, 0) };
            col.Children.Add(Row(L.T("Idioma"), Segmented(new[] { ("pt", "Português"), ("en", "English") }, L.Lang, v =>
            {
                if (v == L.Lang) return;
                L.Set(v);
                _s.Language = v;
                Go(_index, animate: false); // textos no idioma novo
            })));
            bool light = string.Equals(_s.Theme, "Light", StringComparison.OrdinalIgnoreCase);
            col.Children.Add(Row(L.T("Aparência"), Segmented(new[] { ("Dark", L.T("Escuro")), ("Light", L.T("Claro")) }, light ? "Light" : "Dark", v =>
            {
                SettingsWindow.SetTheme(_s, light: v == "Light");
                ThemePalette.Apply(v != "Light");
                ThemePalette.ApplyTo(this);
                UpdateIsland(Step.Look, true);
            })));
            string style = _s.IslandStyle == "Attached" ? "Attached" : "Floating";
            col.Children.Add(Row(L.T("Posição"), Segmented(new[] { ("Attached", L.T("Colada no topo")), ("Floating", L.T("Solta")) }, style, v =>
            {
                _s.IslandStyle = v == "Attached" ? "Attached" : "TopFloating";
                UpdateIsland(Step.Look, true);
            })));
            return Page(L.T("Do seu jeito"), L.T("Escolha agora ou ajuste quando quiser. A ilha lá em cima já mostra como fica."), col);
        }

        private FrameworkElement FeaturesPage()
        {
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-40, 22, -40, 0) };
            void Add(string label, Func<bool> get, Action<bool> set)
            {
                grid.Children.Add(Toggle(label, get(), v => { set(v); UpdateIsland(Step.Features, true); }));
            }
            Add(L.T("Música"), () => _s.ShowMedia, v => _s.ShowMedia = v);
            Add(L.T("Agenda"), () => _s.ShowCalendar, v => _s.ShowCalendar = v);
            Add(L.T("Tarefas do dia"), () => _s.ShowTasks, v => _s.ShowTasks = v);
            Add(L.T("Notas"), () => _s.ShowNotes, v => _s.ShowNotes = v);
            Add(L.T("Foco (timer)"), () => _s.ShowTimer, v => _s.ShowTimer = v);
            Add(L.T("Prateleira"), () => _s.ShowShelf, v => _s.ShowShelf = v);
            Add(L.T("Copiados"), () => _s.ShowClipboard, v => _s.ShowClipboard = v);
            Add(L.T("Teleprompter"), () => _s.ShowPrompter, v => _s.ShowPrompter = v);
            Add(L.T("Espelho (câmera)"), () => _s.ShowMirror, v => _s.ShowMirror = v);
            Add(L.T("Bem-estar"), () => _s.WellnessNudges, v => _s.WellnessNudges = v);
            Add(L.T("CPU e memória"), () => _s.ShowStats, v => _s.ShowStats = v);
            return Page(L.T("O que vai na ilha?"), L.T("Só o que você usa. Menos coisas, ilha mais limpa."), grid);
        }

        private FrameworkElement ReadyPage()
        {
            var tips = new StackPanel { Margin = new Thickness(0, 26, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            void Tip(string keys, string text)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
                var k = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 14, 0), MinWidth = 92 };
                k.SetResourceReference(Border.BackgroundProperty, "Control");
                k.Child = new TextBlock { Text = keys, FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
                DockPanel.SetDock(k, Dock.Left);
                row.Children.Add(k);
                var t = new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                t.SetResourceReference(TextBlock.ForegroundProperty, "Text2");
                row.Children.Add(t);
                tips.Children.Add(row);
            }
            Tip(L.T("Topo da tela"), L.T("Passe o mouse para abrir."));
            Tip("Ctrl Alt N", L.T("Abre e fecha de qualquer lugar."));
            Tip(L.T("Arrastar"), L.T("Solte arquivos na ilha para guardar."));
            string first = FirstName(_s.UserName);
            return Page(first.Length > 0 ? L.F("Tudo pronto, {0}.", first) : L.T("Tudo pronto."), L.T("A ilha já está lá em cima, esperando você."), tips);
        }

        private void Finish()
        {
            if (_done) return;
            if (string.IsNullOrWhiteSpace(_s.UserName) && !_replay) { Go(Array.IndexOf(_steps, Step.Name)); return; }
            _done = true;
            Save();
            if (!SystemParameters.ClientAreaAnimation || !IsLoaded)
            {
                Finished?.Invoke();
                FadeOutAndClose();
                return;
            }
            BecomeIsland();
        }

        /// <summary>
        /// O final: o conteúdo some, o painel preto encolhe até o tamanho da ilha, pega impulso,
        /// sobe e "cola" no topo da tela (achata um pouco e balança, como algo que encaixou).
        /// Um traço de luz corre pela borda do monitor e a ilha de verdade assume o lugar.
        /// </summary>
        private void BecomeIsland()
        {
            _morphing = true;
            _helloTimer.Stop();
            IsHitTestVisible = false;
            double scale = Math.Clamp(_s.Scale, 0.6, 1.6);
            double w = Math.Max(60, _s.CollapsedWidth * scale), h = Math.Max(20, _s.CollapsedHeight * scale);
            bool attached = string.Equals(_s.IslandStyle, "Attached", StringComparison.OrdinalIgnoreCase);
            double top = attached ? 0 : 10 * scale;
#if DEBUG
            if (int.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_Y"), out int testY)) top += testY;
#endif
            double screenH = _stage.ActualHeight;
            double dy = top + h / 2 - screenH / 2; // centro do painel → centro da ilha no topo

            var outEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var t0 = TimeSpan.Zero;
            TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

            // 1. O conteúdo se apaga e a sombra grande some.
            _panel.Child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            ((System.Windows.Media.Effects.DropShadowEffect)_panel.Effect).BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(0.25, Ms(500)));
            _dim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(900)) { BeginTime = Ms(200), EasingFunction = outEase });
            _panel.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
            _panel.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), Ms(600)) { BeginTime = Ms(300) });

            // 2. O painel encolhe até o tamanho da ilha (com mola), ainda no centro.
            var shrink = new SpringEase { Damping = 0.82, Frequency = 7.5 };
            _panel.BeginAnimation(WidthProperty, new DoubleAnimation(PanelW, w, Ms(820)) { BeginTime = Ms(180), EasingFunction = shrink });
            _panel.BeginAnimation(HeightProperty, new DoubleAnimation(PanelH, h, Ms(820)) { BeginTime = Ms(180), EasingFunction = shrink });
            _panel.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0B));
            _panel.Background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(IslandColor(), Ms(600)) { BeginTime = Ms(300) });

            // 3. Pega impulso: desce um pouquinho e achata.
            double charge = 860;
            _panelMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 9, Ms(200)) { BeginTime = Ms(charge), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            _flightScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.08, Ms(200)) { BeginTime = Ms(charge), EasingFunction = outEase });
            _flightScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.86, Ms(200)) { BeginTime = Ms(charge), EasingFunction = outEase });

            // 4. Sobe acelerando e estica no caminho (como uma gota puxada para cima).
            double launch = charge + 200, flight = 430;
            var pull = new CubicEase { EasingMode = EasingMode.EaseIn };
            var up = new DoubleAnimation(9, dy, Ms(flight)) { BeginTime = Ms(launch), EasingFunction = pull };
            _panelMove.BeginAnimation(TranslateTransform.YProperty, up);
            _flightScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimationUsingKeyFrames
            {
                BeginTime = Ms(launch), Duration = Ms(flight),
                KeyFrames =
                {
                    new EasingDoubleKeyFrame(1.08, KeyTime.FromPercent(0)),
                    new EasingDoubleKeyFrame(0.84, KeyTime.FromPercent(0.55), new SineEase { EasingMode = EasingMode.EaseOut }),
                    new EasingDoubleKeyFrame(0.9, KeyTime.FromPercent(1)),
                },
            });
            _flightScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimationUsingKeyFrames
            {
                BeginTime = Ms(launch), Duration = Ms(flight),
                KeyFrames =
                {
                    new EasingDoubleKeyFrame(0.86, KeyTime.FromPercent(0)),
                    new EasingDoubleKeyFrame(1.32, KeyTime.FromPercent(0.55), new SineEase { EasingMode = EasingMode.EaseOut }),
                    new EasingDoubleKeyFrame(1.2, KeyTime.FromPercent(1)),
                },
            });

            // 5. Encosta no topo: cola, achata e balança até assentar.
            double contact = launch + flight;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = Ms(contact) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Land(w, h, top, attached);
            };
            timer.Start();
        }

        private void Land(double w, double h, double top, bool attached)
        {
            TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);
            // O formato de voo volta ao normal na hora; quem achata agora é o "impacto", preso pelo topo.
            _flightScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, Ms(90)));
            _flightScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Ms(90)));
            _impactScale.CenterX = 0;
            _impactScale.CenterY = -h / 2; // a borda de cima da ilha (a origem do painel é o centro)
            var wobble = new SpringEase { Damping = 0.32, Frequency = 15 };
            _impactScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.2, 1, Ms(900)) { EasingFunction = wobble });
            _impactScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.62, 1, Ms(900)) { EasingFunction = wobble });

            // Os cantos de cima ficam retos (colada na borda).
            if (attached)
            {
                var start = DateTime.Now;
                EventHandler? step = null;
                step = (_, _) =>
                {
                    _attach = Math.Min(1, (DateTime.Now - start).TotalMilliseconds / 140);
                    if (_attach >= 1) CompositionTarget.Rendering -= step;
                };
                CompositionTarget.Rendering += step;
            }

            // Um traço de luz corre pela borda da tela, saindo da ilha para os dois lados.
            var accent = ((SolidColorBrush)Resources["Accent"]).Color;
            var line = new Border
            {
                Height = 2, Width = w, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, top + (attached ? 0 : h - 1), 0, 0), IsHitTestVisible = false,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 0),
                        new GradientStop(Color.FromArgb(0xE6, accent.R, accent.G, accent.B), 0.32),
                        new GradientStop(Color.FromArgb(0x10, accent.R, accent.G, accent.B), 0.5),
                        new GradientStop(Color.FromArgb(0xE6, accent.R, accent.G, accent.B), 0.68),
                        new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1),
                    },
                },
            };
            _stage.Children.Insert(1, line);
            var spread = new DoubleAnimation(w, Math.Min(_stage.ActualWidth, w + 900), Ms(950)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            line.BeginAnimation(WidthProperty, spread);
            line.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, Ms(950)) { BeginTime = Ms(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });

            // Um brilho curto embaixo da ilha, da cor de destaque.
            var glow = (System.Windows.Media.Effects.DropShadowEffect)_panel.Effect;
            glow.Color = accent;
            glow.ShadowDepth = 0;
            glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(10, 34, Ms(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                Duration = Ms(900),
                KeyFrames =
                {
                    new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)),
                    new EasingDoubleKeyFrame(0.45, KeyTime.FromPercent(0.18)),
                    new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }),
                },
            });

            // Assentou: a ilha de verdade assume (quem abriu a apresentação chama FadeOutAndClose).
            var settle = new System.Windows.Threading.DispatcherTimer { Interval = Ms(760) };
            settle.Tick += (_, _) =>
            {
                settle.Stop();
                _morphing = false;
                Finished?.Invoke();
            };
            settle.Start();
        }

        /// <summary>A cor da ilha escolhida (preta no tema escuro, clara no claro).</summary>
        private Color IslandColor()
        {
            try { if (ColorConverter.ConvertFromString(_s.IslandColor) is Color c) return Color.FromRgb(c.R, c.G, c.B); } catch { }
            return IslandLight ? Color.FromRgb(0xF2, 0xF2, 0xF5) : Colors.Black;
        }

        /// <summary>Some depressa, já com a ilha de verdade no lugar.</summary>
        public void FadeOutAndClose(int delayMs = 0)
        {
            if (_closed) return;
            if (!IsLoaded) { Close(); return; }
            bool motion = SystemParameters.ClientAreaAnimation;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(motion ? 380 : 0))
            {
                BeginTime = TimeSpan.FromMilliseconds(motion ? delayMs : 0),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }

        private void Save()
        {
            if (!_nameOnly)
            {
                _s.OnboardingDone = true;
                _s.NewIntroSeen = true;
                _s.Language = L.Lang;
                _s.LastSeenVersion = WhatsNewWindow.CurrentVersion; // instalação nova: sem tela de novidades
            }
            _s.Save();
        }

        // ───────────── A ilha de demonstração ─────────────

        private void BuildScreen()
        {
            // Papel de parede discreto: um degradê suave com um brilho da cor de destaque.
            var wall = new Border { CornerRadius = new CornerRadius(0) };
            wall.SetResourceReference(Border.BackgroundProperty, "CardBg");
            _screen.Children.Add(wall);
            var glow = new Border
            {
                Background = new RadialGradientBrush(Color.FromArgb(0x30, 0x7C, 0xE3, 0xA1), Colors.Transparent)
                {
                    Center = new Point(0.5, 0), GradientOrigin = new Point(0.5, 0), RadiusX = 0.55, RadiusY = 1.1,
                },
            };
            _screen.Children.Add(glow);
            var fade = new Border { Height = 70, VerticalAlignment = VerticalAlignment.Bottom };
            fade.SetResourceReference(Border.BackgroundProperty, "WinBg");
            fade.OpacityMask = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90);
            _screen.Children.Add(fade);

            _island.HorizontalAlignment = HorizontalAlignment.Center;
            _island.VerticalAlignment = VerticalAlignment.Top;
            _island.Width = 150;
            _island.Height = 34;
            _island.ClipToBounds = true;
            _island.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Opacity = 0.35, Direction = 270 };
            _island.Child = _islandContent;
            _screen.Children.Add(_island);
        }

        private bool IslandLight => string.Equals(_s.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        private bool IslandFloating => _s.IslandStyle != "Attached";

        private void UpdateIsland(Step step, bool animate)
        {
            bool light = IslandLight, floating = IslandFloating;
            _island.Background = new SolidColorBrush(light ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x05, 0x05, 0x06));
            _island.BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x22, 0, 0, 0) : Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            _island.BorderThickness = new Thickness(1);
            // Dentro da ilha, a cor do traço segue a ilha (e não a janela).
            _island.Resources["Ink_FF"] = new SolidColorBrush(light ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White);
            _island.Resources["Accent"] = Resources["Accent"];
            var ink = light ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White;
            _islandContent.Children.Clear();

            switch (step)
            {
                case Step.Welcome:
                    Size(150, 34, animate);
                    _islandContent.Children.Add(Breathing(ink));
                    break;
                case Step.Name:
                    ShowGreeting(_nameBox?.Text.Trim() ?? _s.UserName.Trim());
                    return;
                case Step.Look:
                    Size(420, 120, animate);
                    _islandContent.Children.Add(MiniPlayer(ink));
                    break;
                case Step.Features:
                    Size(420, 120, animate);
                    _islandContent.Children.Add(MiniTabs(ink));
                    break;
                default:
                    ShowNudgeContent("sparkle", L.T("Tudo pronto"), L.T("A ilha está pronta para o seu dia."), ink, animate);
                    break;
            }
            Place(floating, animate);
        }

        private void ShowGreeting(string name)
        {
            if (_steps[_index] != Step.Name) return;
            var ink = IslandLight ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White;
            int h = DateTime.Now.Hour;
            string hello = h >= 5 && h < 12 ? L.T("Bom dia") : h >= 12 && h < 18 ? L.T("Boa tarde") : L.T("Boa noite");
            string first = FirstName(name);
            bool had = _islandContent.Children.Count > 0 && _islandContent.Tag as string == "greeting";
            if (had && _islandContent.Children[0] is FrameworkElement row && row.FindName("Title") is TextBlock title)
            {
                title.Text = first.Length > 0 ? $"{hello}, {first}" : hello;
                FitGreeting(title.Text);
                return;
            }
            ShowNudgeContent(NudgeArt.GreetingKind(h), first.Length > 0 ? $"{hello}, {first}" : hello, L.T("Que bom ter você aqui."), ink, true);
            _islandContent.Tag = "greeting";
            Place(IslandFloating, true);
        }

        private void FitGreeting(string title)
        {
            var ft = new FormattedText(title, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 15, Brushes.White, 1);
            Size(Math.Clamp(ft.Width + 120, 260, 520), 66, true);
        }

        private void ShowNudgeContent(string kind, string title, string text, Color ink, bool animate)
        {
            _islandContent.Children.Clear();
            _islandContent.Tag = null;
            var grid = new Grid { Margin = new Thickness(16, 0, 18, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new Border { Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Child = NudgeArt.Build(kind) });
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock { Name = "Title", Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink), TextTrimming = TextTrimming.CharacterEllipsis };
            col.Children.Add(t);
            col.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0x99, ink.R, ink.G, ink.B)), Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(col, 1);
            grid.Children.Add(col);
            NameScope.SetNameScope(grid, new NameScope());
            grid.RegisterName("Title", t);
            _islandContent.Children.Add(grid);
            FitGreeting(title);
        }

        private FrameworkElement Breathing(Color ink)
        {
            // Fechada: só um ponto de luz que respira, convidando a passar o mouse.
            var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            dot.SetResourceReference(Border.BackgroundProperty, "Accent");
            if (SystemParameters.ClientAreaAnimation)
                dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            return dot;
        }

        private FrameworkElement MiniPlayer(Color ink)
        {
            var g = new Grid { Margin = new Thickness(18, 16, 18, 16) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new Border
            {
                Width = 86, Height = 86, CornerRadius = new CornerRadius(14),
                Background = new LinearGradientBrush(Color.FromRgb(0xFF, 0x7A, 0x59), Color.FromRgb(0x8E, 0x3C, 0xF7), 45),
            });
            var info = new StackPanel { Margin = new Thickness(16, 6, 0, 0) };
            info.Children.Add(new TextBlock { Text = "Maré Baixa", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink) });
            info.Children.Add(new TextBlock { Text = "Costa Verde", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0x99, ink.R, ink.G, ink.B)), Margin = new Thickness(0, 2, 0, 0) });
            var bar = new Grid { Height = 3, Margin = new Thickness(0, 16, 0, 0) };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromArgb(0x30, ink.R, ink.G, ink.B)) });
            var fill = new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(ink), HorizontalAlignment = HorizontalAlignment.Left, Width = 40 };
            if (SystemParameters.ClientAreaAnimation)
                fill.BeginAnimation(WidthProperty, new DoubleAnimation(30, 220, TimeSpan.FromSeconds(12)) { RepeatBehavior = RepeatBehavior.Forever });
            bar.Children.Add(fill);
            info.Children.Add(bar);
            Grid.SetColumn(info, 1);
            g.Children.Add(info);
            return g;
        }

        private FrameworkElement MiniTabs(Color ink)
        {
            var tabs = new List<string>();
            if (_s.ShowMedia) tabs.Add("FillHome");
            if (_s.ShowShelf) tabs.Add("IcoTray");
            if (_s.ShowCalendar) tabs.Add("FillCalendar");
            if (_s.ShowTasks) tabs.Add("FillTasks");
            if (_s.ShowNotes) tabs.Add("FillNotes");
            if (_s.ShowTimer) tabs.Add("FillTimer");
            if (_s.ShowClipboard) tabs.Add("FillClip");
            if (_s.ShowMirror) tabs.Add("FillCamera");
            if (_s.ShowPrompter) tabs.Add("FillPrompter");
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            int i = 0;
            foreach (var key in tabs)
            {
                var tile = new Border
                {
                    Width = 38, Height = 32, CornerRadius = new CornerRadius(10), Margin = new Thickness(3, 0, 3, 0),
                    Background = new SolidColorBrush(Color.FromArgb(i == 0 ? (byte)0x26 : (byte)0x0F, ink.R, ink.G, ink.B)),
                    Child = new Glyph { Data = (Geometry)FindResource(key), Size = 16, Filled = key.StartsWith("Fill"), Thickness = key.StartsWith("Fill") ? 0 : 1.8, Foreground = new SolidColorBrush(ink) },
                };
                if (SystemParameters.ClientAreaAnimation)
                {
                    var s = new ScaleTransform(0.6, 0.6, 19, 16);
                    tile.RenderTransform = s;
                    var pop = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(i * 40), EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
                    s.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                    s.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                }
                row.Children.Add(tile);
                i++;
            }
            if (tabs.Count == 0)
                row.Children.Add(new TextBlock { Text = L.T("Escolha pelo menos um recurso"), FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromArgb(0x99, ink.R, ink.G, ink.B)) });
            Size(Math.Max(300, tabs.Count * 44 + 60), 90, true);
            return row;
        }

        private void Size(double w, double h, bool animate)
        {
            var dur = TimeSpan.FromMilliseconds(animate && SystemParameters.ClientAreaAnimation ? 620 : 0);
            var ease = new SpringEase { Damping = 0.78, Frequency = 7 };
            _island.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = ease });
            _island.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
            double r = Math.Min(22, h * 0.42);
            _island.CornerRadius = IslandFloating ? new CornerRadius(r) : new CornerRadius(0, 0, r, r);
        }

        private void Place(bool floating, bool animate)
        {
            var target = new Thickness(0, floating ? 22 : 0, 0, 0);
            if (!animate || !SystemParameters.ClientAreaAnimation) { _island.Margin = target; }
            else _island.BeginAnimation(MarginProperty, new ThicknessAnimation(target, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            double r = Math.Min(22, _island.Height * 0.42);
            _island.CornerRadius = floating ? new CornerRadius(r) : new CornerRadius(0, 0, r, r);
        }

        // ───────────── Controles ─────────────

        private FrameworkElement Row(string label, FrameworkElement control)
        {
            var row = new DockPanel { Margin = new Thickness(40, 0, 40, 14) };
            var t = new TextBlock { Text = label, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(control, Dock.Right);
            row.Children.Add(control);
            row.Children.Add(t);
            return row;
        }

        private FrameworkElement Segmented((string Value, string Label)[] options, string current, Action<string> changed)
        {
            var host = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(3) };
            host.SetResourceReference(Border.BackgroundProperty, "Control");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var buttons = new List<Button>();
            foreach (var (value, label) in options)
            {
                var b = new Button { Content = label, Tag = value, Template = SegTemplate(), Padding = new Thickness(14, 6, 14, 6), FontSize = 13.5, Cursor = Cursors.Hand, Focusable = false };
                buttons.Add(b);
                row.Children.Add(b);
            }
            void Mark(string v)
            {
                foreach (var b in buttons)
                {
                    bool on = (string)b.Tag == v;
                    if (on) b.SetResourceReference(BackgroundProperty, "CardBg"); else b.Background = Brushes.Transparent;
                    b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                    b.SetResourceReference(ForegroundProperty, on ? "WinFg" : "Text2");
                }
            }
            foreach (var b in buttons) b.Click += (_, _) => { Mark((string)b.Tag); changed((string)b.Tag); };
            Mark(current);
            host.Child = row;
            return host;
        }

        private FrameworkElement Toggle(string label, bool on, Action<bool> changed)
        {
            var track = new Border { Width = 38, Height = 22, CornerRadius = new CornerRadius(11) };
            var knob = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0) };
            knob.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.25 };
            var move = new TranslateTransform(on ? 16 : 0, 0);
            knob.RenderTransform = move;
            var sw = new Grid { Children = { track, knob }, VerticalAlignment = VerticalAlignment.Center };
            void Paint(bool v) => track.SetResourceReference(Border.BackgroundProperty, v ? "Accent" : "Line");
            Paint(on);
            var t = new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            DockPanel.SetDock(sw, Dock.Right);
            var row = new DockPanel { Children = { sw, t } };
            var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(13, 9, 11, 9), Margin = new Thickness(4), Cursor = Cursors.Hand, Child = row };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            bool state = on;
            card.MouseLeftButtonUp += (_, _) =>
            {
                state = !state;
                Paint(state);
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(state ? 16 : 0, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 240 : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                changed(state);
            };
            return card;
        }

        private Button PrimaryButton()
        {
            var b = new Button
            {
                Template = (ControlTemplate)XamlReader.Parse(
                    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                    "<Grid><Border x:Name='Bg' CornerRadius='12' Background='{TemplateBinding Background}'/>" +
                    "<Border x:Name='Hover' CornerRadius='12' Background='#FFFFFF' Opacity='0'/>" +
                    "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid>" +
                    "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Hover' Property='Opacity' Value='0.12'/></Trigger>" +
                    "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='Bg' Property='Opacity' Value='0.35'/></Trigger></ControlTemplate.Triggers></ControlTemplate>"),
                Width = 260, Height = 44, FontSize = 15, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)),
            };
            b.SetResourceReference(BackgroundProperty, "Accent");
            return b;
        }

        private Button TextButton(string text)
        {
            var b = new Button
            {
                Content = text, FontSize = 13.5, Cursor = Cursors.Hand, Focusable = false, Padding = new Thickness(8, 6, 8, 6),
                Template = (ControlTemplate)XamlReader.Parse(
                    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
                    "<Border Background='Transparent' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border></ControlTemplate>"),
            };
            b.SetResourceReference(ForegroundProperty, "Text2");
            return b;
        }

        private static ControlTemplate SegTemplate() => (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
            "<Border CornerRadius='8' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>" +
            "<ContentPresenter HorizontalAlignment='Center'/></Border></ControlTemplate>");

        private void ApplyAccent()
        {
            var accent = Color.FromRgb(0x7C, 0xE3, 0xA1);
            try { if (ColorConverter.ConvertFromString(_s.AccentColor) is Color c) accent = c; } catch { }
            Resources["Accent"] = new SolidColorBrush(accent);
        }

        private static string FirstName(string n) =>
            (n ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
