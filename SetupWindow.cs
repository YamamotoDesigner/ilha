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

        public event Action? Finished;

        public SetupWindow(AppSettings settings, bool replay = false, bool nameOnly = false)
        {
            _s = settings;
            _replay = replay;
            _nameOnly = nameOnly;
            _steps = nameOnly ? new[] { Step.Name } : new[] { Step.Welcome, Step.Name, Step.Look, Step.Features, Step.Ready };
            Title = "Ilha";
            Width = 680;
            Height = 640;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SetResourceReference(FontFamilyProperty, "UiFont");
            SetResourceReference(BackgroundProperty, "WinBg");
            SetResourceReference(ForegroundProperty, "WinFg");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 48, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
            });
            ThemePalette.ApplyTo(this);
            ApplyAccent();
            SourceInitialized += (_, _) =>
            {
                int round = 2;
                try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref round, sizeof(int)); } catch { }
            };

            var root = new Grid();
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
            Content = root;

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && _next.IsEnabled) { Next(); e.Handled = true; }
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
            };
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
            Close();
            Finished?.Invoke();
        }

        private void Save()
        {
            if (!_nameOnly)
            {
                _s.OnboardingDone = true;
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
