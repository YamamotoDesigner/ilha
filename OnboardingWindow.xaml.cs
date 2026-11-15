using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Ilha
{
    /// <summary>
    /// Apresentação da primeira vez: idioma, claro/escuro, estilo da ilha e recursos, com uma
    /// ilha de demonstração animada no topo. As escolhas valem na hora.
    /// </summary>
    public partial class OnboardingWindow : Window
    {
        private const int StepCount = 5;
        private readonly AppSettings _settings;
        private readonly bool _replay;
        private readonly DispatcherTimer _demo = new() { Interval = TimeSpan.FromSeconds(2.6) };
        private readonly List<(CheckBox Box, Action<bool> Set, string Label)> _features = new();
        private int _step;
        private bool _demoOpen, _finished;

        /// <summary>Terminou (botão Começar, X ou fechar a janela).</summary>
        public event Action? Finished;

        public OnboardingWindow(AppSettings settings, bool replay)
        {
            InitializeComponent();
            _settings = settings;
            _replay = replay;
            ThemePalette.ApplyTo(this);
            SourceInitialized += (_, _) =>
            {
                int round = 2; // cantos arredondados do Windows 11
                try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref round, sizeof(int)); } catch { }
            };

            // Escolhas atuais
            (L.English ? PickEn : PickPt).IsChecked = true;
            bool light = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
            (light ? PickLight : PickDark).IsChecked = true;
            (_settings.IslandStyle switch { "Floating" => PickFree, "TopFloating" => PickTop, _ => PickAttached }).IsChecked = true;

            BuildFeatures();
            BuildDots();
            BuildBars();
            ApplyTexts();
            ApplyDemoTheme(light, animate: false);
            ApplyDemoStyle(animate: false);
            StartAmbient();
            _demo.Tick += (_, _) => ToggleDemo();
            Loaded += (_, _) =>
            {
                int first = 0;
#if DEBUG
                if (int.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_STEP"), out int testStep)) first = testStep;
                if (Environment.GetEnvironmentVariable("ILHA_TEST_DEMO") == "open") ToggleDemo();
                if (Environment.GetEnvironmentVariable("ILHA_TEST_SHOT") is { Length: > 0 } shot)
                {
                    var snap = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
                    snap.Tick += (_, _) =>
                    {
                        snap.Stop();
                        _demo.Stop();
                        var root = (FrameworkElement)Content;
                        var dpi = VisualTreeHelper.GetDpi(this);
                        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                            (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
                            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle((Brush)FindResource("WinBg"), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                        }
                        bmp.Render(dv);
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                        using var fs = System.IO.File.Create(shot);
                        enc.Save(fs);
                    };
                    snap.Start();
                }
                if (Environment.GetEnvironmentVariable("ILHA_TEST_LIGHT") == "1") PickLight.IsChecked = true;
                if (Environment.GetEnvironmentVariable("ILHA_TEST_EN") == "1") PickEn.IsChecked = true;
#endif
                ShowStep(first, forward: true);
                _demo.Start();
            };
        }

        // ───────────── Textos (mudam junto com o idioma) ─────────────

        private void ApplyTexts()
        {
            Title = L.T("Bem-vindo ao Ilha");
            WelcomeTitle.Text = L.T("Olá! Este é o Ilha.");
            WelcomeText.Text = L.T("O topo da sua tela, finalmente útil: música, agenda, notas e foco a um passar de mouse. Para começar, escolha o idioma.");
            ThemeTitle.Text = L.T("Claro ou escuro?");
            ThemeText.Text = L.T("Combine a ilha com o seu Windows. Dá para mudar quando quiser nas configurações.");
            DarkTitle.Text = L.T("Escuro");
            LightTitle.Text = L.T("Claro");
            StyleTitle.Text = L.T("Onde a ilha fica?");
            StyleText.Text = L.T("Escolha como ela aparece no topo da tela.");
            AttachedTitle.Text = L.T("Colada no topo");
            AttachedHint.Text = L.T("Presa na borda, como um entalhe");
            TopTitle.Text = L.T("Solta no topo");
            TopHint.Text = L.T("Um retângulo arredondado, centralizado");
            FreeTitle.Text = L.T("Livre");
            FreeHint.Text = L.T("Arraste e redimensione onde quiser");
            FeaturesTitle.Text = L.T("O que você quer na ilha?");
            FeaturesText.Text = L.T("Deixe só o que usa. Menos coisas, ilha mais limpa.");
            DoneTitle.Text = L.T("Tudo pronto!");
            DoneText.Text = L.T("Três dicas para começar:");
            DemoArtist.Text = L.T("Ilha FM");
            DemoDay.Text = DateTime.Today.ToString("ddd").TrimEnd('.').ToUpperInvariant();
            foreach (var f in _features) f.Box.Content = L.T(f.Label);
            BuildTips();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            BackButton.Content = L.T("Voltar");
            BackButton.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
            NextButton.Content = _step < StepCount - 1 ? L.T("Continuar") : _replay ? L.T("Concluir") : L.T("Começar");
        }

        // ───────────── Passos ─────────────

        private FrameworkElement StepPanel(int i) => i switch
        {
            0 => Step0, 1 => Step1, 2 => Step2, 3 => Step3, _ => Step4,
        };

        private void ShowStep(int step, bool forward)
        {
            _step = Math.Clamp(step, 0, StepCount - 1);
            for (int i = 0; i < StepCount; i++)
                StepPanel(i).Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;

            // Entrada: desliza e aparece; os cartões chegam um depois do outro.
            var panel = StepPanel(_step);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var shift = new TranslateTransform(forward ? 36 : -36, 0);
            panel.RenderTransform = shift;
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });

            var cards = panel.Children().OfType<Panel>().SelectMany(p => p.Children.OfType<FrameworkElement>())
                .Where(e => e is RadioButton or CheckBox or Border).ToList();
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var lift = new TranslateTransform(0, 14);
                card.RenderTransform = lift;
                card.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(120 + i * 55);
                card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)) { BeginTime = delay });
                lift.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(460)) { BeginTime = delay, EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } });
            }

            UpdateDots();
            UpdateButtons();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_step == StepCount - 1) Finish();
            else ShowStep(_step + 1, forward: true);
        }

        private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1, forward: false);

        private void Skip_Click(object sender, RoutedEventArgs e) => Finish();

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            _settings.OnboardingDone = true;
            _settings.Language = L.Lang;
            _settings.Save();
            _demo.Stop();
            Finished?.Invoke();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Finish(); // fechar pelo Alt+F4 também conta como "começar"
        }

        // ───────────── Escolhas ─────────────

        private void Lang_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            L.Set(sender == PickEn ? "en" : "pt");
            _settings.Language = L.Lang;
            ApplyTexts();
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            bool light = sender == PickLight;
            SettingsWindow.SetTheme(_settings, light);
            ThemePalette.Apply(!light);
            ThemePalette.ApplyTo(this);
            ApplyDemoTheme(light, animate: true);
        }

        private void Style_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            _settings.IslandStyle = sender == PickFree ? "Floating" : sender == PickTop ? "TopFloating" : "Attached";
            ApplyDemoStyle(animate: true);
        }

        private void BuildFeatures()
        {
            var s = _settings;
            Add("Música", s.ShowMedia, v => s.ShowMedia = v);
            Add("Agenda e tarefas", s.ShowCalendar, v => s.ShowCalendar = v);
            Add("Notas", s.ShowNotes, v => s.ShowNotes = v);
            Add("Foco (timer)", s.ShowTimer, v => s.ShowTimer = v);
            Add("Teleprompter", s.ShowPrompter, v => s.ShowPrompter = v);
            Add("Prateleira", s.ShowShelf, v => s.ShowShelf = v);
            Add("Copiados", s.ShowClipboard, v => s.ShowClipboard = v);
            Add("Espelho (câmera)", s.ShowMirror, v => s.ShowMirror = v);
            Add("CPU e memória", s.ShowStats, v => s.ShowStats = v);

            void Add(string label, bool on, Action<bool> set)
            {
                var box = new CheckBox { Style = (Style)FindResource("Feature"), IsChecked = on, Content = L.T(label) };
                box.Checked += (_, _) => set(true);
                box.Unchecked += (_, _) => set(false);
                FeatureGrid.Children.Add(box);
                _features.Add((box, set, label));
            }
        }

        private void BuildTips()
        {
            Tips.Children.Clear();
            AddTip(L.T("Passe o mouse no topo da tela para abrir a ilha."));
            AddTip(L.T("Ctrl + Alt + N abre e fecha de qualquer lugar."));
            AddTip(L.T("Arraste arquivos até a ilha para guardar na prateleira."));

            void AddTip(string text)
            {
                var row = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Background = (Brush)FindResource("CardBg"),
                    BorderBrush = (Brush)FindResource("Line"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 11, 14, 11),
                    Margin = new Thickness(0, 0, 0, 8),
                };
                row.SetResourceReference(Border.BackgroundProperty, "CardBg");
                row.SetResourceReference(Border.BorderBrushProperty, "Line");
                var dock = new DockPanel();
                var mark = new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(2.5),
                    Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                };
                mark.SetResourceReference(Border.BackgroundProperty, "Accent");
                dock.Children.Add(mark);
                dock.Children.Add(new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap });
                row.Child = dock;
                Tips.Children.Add(row);
            }
        }

        // ───────────── Indicador de progresso ─────────────

        private void BuildDots()
        {
            for (int i = 0; i < StepCount; i++)
            {
                var dot = new Border { Height = 6, Width = 6, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 6, 0) };
                Dots.Children.Add(dot);
            }
        }

        private void UpdateDots()
        {
            for (int i = 0; i < StepCount; i++)
            {
                var dot = (Border)Dots.Children[i];
                bool on = i == _step;
                dot.SetResourceReference(Border.BackgroundProperty, on ? "Accent" : "ControlOff");
                dot.BeginAnimation(WidthProperty, new DoubleAnimation(on ? 20 : 6, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            }
        }

        // ───────────── Ilha de demonstração e fundo ─────────────

        private void BuildBars()
        {
            double[] periods = { 0.42, 0.58, 0.36, 0.5 };
            foreach (var p in periods)
            {
                var bar = new Rectangle
                {
                    Width = 3, Height = 12, RadiusX = 1, RadiusY = 1, Margin = new Thickness(1.5, 0, 1.5, 0),
                    Fill = new SolidColorBrush(Color.FromRgb(0x7C, 0xE3, 0xA1)), RenderTransformOrigin = new Point(0.5, 0.5),
                };
                var scale = new ScaleTransform(1, 0.4);
                bar.RenderTransform = scale;
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(p))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase(),
                });
                DemoBars.Children.Add(bar);
            }
        }

        /// <summary>A ilha de demonstração abre e fecha sozinha, com a mesma mola do app.</summary>
        private void ToggleDemo()
        {
            _demoOpen = !_demoOpen;
            var spring = new SpringEase();
            var dur = TimeSpan.FromMilliseconds(620);
            DemoIsland.BeginAnimation(WidthProperty, new DoubleAnimation(_demoOpen ? 430 : 130, dur) { EasingFunction = spring });
            DemoIsland.BeginAnimation(HeightProperty, new DoubleAnimation(_demoOpen ? 92 : 30, dur) { EasingFunction = spring });
            DemoMini.BeginAnimation(OpacityProperty, new DoubleAnimation(_demoOpen ? 0 : 1, TimeSpan.FromMilliseconds(_demoOpen ? 120 : 260))
            {
                BeginTime = TimeSpan.FromMilliseconds(_demoOpen ? 0 : 200),
            });
            DemoFull.BeginAnimation(OpacityProperty, new DoubleAnimation(_demoOpen ? 1 : 0, TimeSpan.FromMilliseconds(_demoOpen ? 280 : 100))
            {
                BeginTime = TimeSpan.FromMilliseconds(_demoOpen ? 160 : 0),
            });
            if (_demoOpen)
                DemoProgress.BeginAnimation(WidthProperty, new DoubleAnimation(30, 150, TimeSpan.FromSeconds(2.4)));
        }

        private void ApplyDemoTheme(bool light, bool animate)
        {
            var island = light ? Color.FromRgb(0xF7, 0xF7, 0xFA) : Colors.Black;
            var ink = light ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White;
            var bg = new SolidColorBrush(((SolidColorBrush)DemoIsland.Background).Color);
            DemoIsland.Background = bg;
            if (animate) bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(island, TimeSpan.FromMilliseconds(400)));
            else bg.Color = island;
            DemoSong.Foreground = new SolidColorBrush(ink);
            DemoArtist.Foreground = new SolidColorBrush(Color.FromArgb(0xA6, ink.R, ink.G, ink.B));
            DemoProgress.Background = new SolidColorBrush(ink);
        }

        private void ApplyDemoStyle(bool animate)
        {
            string style = _settings.IslandStyle;
            bool detached = style is "Floating" or "TopFloating";
            DemoIsland.CornerRadius = detached ? new CornerRadius(12) : new CornerRadius(0, 0, 12, 12);
            var to = new Thickness(style == "Floating" ? -140 : 0, detached ? 16 : 0, 0, 0);
            if (animate)
                DemoIsland.BeginAnimation(MarginProperty, new ThicknessAnimation(to, TimeSpan.FromMilliseconds(520)) { EasingFunction = new SpringEase() });
            else
                DemoIsland.Margin = to;
        }

        /// <summary>Bolhas de cor do fundo flutuando devagar.</summary>
        private void StartAmbient()
        {
            Drift(BlobA, 70, 30, 7.5);
            Drift(BlobB, -90, 25, 9);

            static void Drift(UIElement blob, double dx, double dy, double seconds)
            {
                var t = (TranslateTransform)blob.RenderTransform;
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, TimeSpan.FromSeconds(seconds))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dy, TimeSpan.FromSeconds(seconds * 0.8))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    internal static class PanelExtensions
    {
        /// <summary>Filhos diretos de um painel (vazio se não for painel).</summary>
        public static IEnumerable<UIElement> Children(this FrameworkElement element) =>
            element is Panel p ? p.Children.Cast<UIElement>() : Enumerable.Empty<UIElement>();
    }
}
