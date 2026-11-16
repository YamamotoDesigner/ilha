using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace Ilha
{
    /// <summary>
    /// "Novidades": aparece uma vez depois de cada atualização, com uma página por novidade,
    /// passando para o lado (setas, rodinha do mouse ou botões).
    /// </summary>
    public sealed class WhatsNewWindow : Window
    {
        private const double PageW = 520;
        private readonly ReleaseNotes.Slide[] _slides;
        private readonly string _version;
        private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal };
        private readonly TranslateTransform _shift = new();
        private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _next, _back;
        private readonly FrameworkElement[] _pages;
        private int _index = -1;
        private int _wheel;

        public WhatsNewWindow(AppSettings settings, string version, ReleaseNotes.Slide[] slides)
        {
            _slides = slides;
            _version = version;
            Title = L.T("Novidades da Ilha");
            Width = PageW;
            Height = 560;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontSize = 13.5;
            SetResourceReference(FontFamilyProperty, "UiFont");
            SetResourceReference(BackgroundProperty, "WinBg");
            SetResourceReference(ForegroundProperty, "WinFg");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 44, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
            });
            ThemePalette.ApplyTo(this);
            Resources["Accent"] = new SolidColorBrush(ParseColor(settings.AccentColor));
            SourceInitialized += (_, _) =>
            {
                int round = 2; // cantos arredondados do Windows 11
                try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref round, sizeof(int)); } catch { }
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });

            // Fechar
            var close = MakeButton(primary: false);
            close.Content = new Glyph { Data = (Geometry)FindResource("IcoClose"), Size = 12, Thickness = 2.4 };
            close.Width = 32; close.Height = 30; close.Padding = new Thickness(0);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 8, 10, 0);
            close.ToolTip = L.T("Fechar");
            WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            close.Click += (_, _) => Close();
            root.Children.Add(close);

            // Páginas
            _pages = slides.Select((s, i) => BuildPage(s, i == 0)).ToArray();
            foreach (var p in _pages) _strip.Children.Add(p);
            _strip.RenderTransform = _shift;
            var viewport = new Canvas { ClipToBounds = true, Background = Brushes.Transparent };
            viewport.Children.Add(_strip);
            viewport.SizeChanged += (_, e) => { foreach (FrameworkElement p in _strip.Children) p.Height = e.NewSize.Height; };
            Grid.SetRow(viewport, 1);
            root.Children.Add(viewport);

            // Rodapé: voltar · pontinhos · continuar
            var footer = new Grid { Margin = new Thickness(28, 0, 28, 22) };
            _back = MakeButton(primary: false);
            _back.Content = L.T("Voltar");
            _back.HorizontalAlignment = HorizontalAlignment.Left;
            _back.Click += (_, _) => Go(_index - 1);
            _next = MakeButton(primary: true);
            _next.HorizontalAlignment = HorizontalAlignment.Right;
            _next.Click += (_, _) => { if (_index >= _slides.Length - 1) Close(); else Go(_index + 1); };
            for (int i = 0; i < slides.Length; i++)
            {
                int target = i;
                var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(4, 0, 4, 0), Cursor = Cursors.Hand };
                dot.MouseLeftButtonUp += (_, _) => Go(target);
                _dots.Children.Add(dot);
            }
            footer.Children.Add(_back);
            footer.Children.Add(_dots);
            footer.Children.Add(_next);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            Content = root;

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Right or Key.Enter or Key.Space) { if (_index < _slides.Length - 1) Go(_index + 1); else Close(); e.Handled = true; }
                else if (e.Key == Key.Left) { Go(_index - 1); e.Handled = true; }
                else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            };
            PreviewMouseWheel += (_, e) =>
            {
                _wheel += e.Delta;
                if (Math.Abs(_wheel) < 120) return;
                Go(_index + (_wheel < 0 ? 1 : -1));
                _wheel = 0;
            };
            Loaded += (_, _) =>
            {
                Go(0);
#if DEBUG
                if (int.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_WNSTEP"), out int step)) Go(step);
#endif
            };
        }

        public static string CurrentVersion
        {
            get
            {
                var v = UpdateService.CurrentVersion;
                return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
            }
        }

        private FrameworkElement BuildPage(ReleaseNotes.Slide s, bool first)
        {
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = PageW - 96 };

            // Ícone num bloco da cor de destaque, com brilho suave
            var tile = new Border
            {
                Width = 76, Height = 76, CornerRadius = new CornerRadius(22),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Glyph
                {
                    Data = (Geometry)FindResource(s.Icon), Size = 36, Filled = s.Filled, Thickness = s.Filled ? 0 : 2.2,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var accent = ((SolidColorBrush)Resources["Accent"]).Color;
            tile.Background = new LinearGradientBrush(Color.FromArgb(0x4D, accent.R, accent.G, accent.B), Color.FromArgb(0x1A, accent.R, accent.G, accent.B), 90);
            tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B));
            tile.BorderThickness = new Thickness(1);
            ((Glyph)tile.Child).SetResourceReference(ForegroundProperty, "Accent");
            col.Children.Add(tile);

            if (first)
            {
                var badge = new TextBlock
                {
                    Text = L.F("VERSÃO {0}", _version.EndsWith(".0") ? _version[..^2] : _version),
                    FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 18, 0, 0),
                };
                badge.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                col.Children.Add(badge);
            }

            col.Children.Add(new TextBlock
            {
                Text = L.T(s.Title), FontSize = 27, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, first ? 6 : 22, 0, 0),
            });
            var body = new TextBlock
            {
                Text = L.T(s.Body), FontSize = 14.5, LineHeight = 21, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
            };
            body.SetResourceReference(TextBlock.ForegroundProperty, "Text2");
            col.Children.Add(body);

            if (s.Bullets is { Length: > 0 })
            {
                var list = new StackPanel();
                foreach (var b in s.Bullets)
                {
                    var check = new Glyph { Data = (Geometry)FindResource("IcoCheck"), Size = 15, Thickness = 2.6, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                    check.SetResourceReference(ForegroundProperty, "Accent");
                    var text = new TextBlock { Text = L.T(b), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(check, Dock.Left);
                    list.Children.Add(new DockPanel { Margin = new Thickness(0, 4, 0, 4), Children = { check, text } });
                }
                var card = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 18, 0, 0), BorderThickness = new Thickness(1), Child = list };
                card.SetResourceReference(Border.BackgroundProperty, "CardBg");
                card.SetResourceReference(Border.BorderBrushProperty, "Line");
                col.Children.Add(card);
            }

            return new Grid { Width = PageW, Children = { col } };
        }

        private void Go(int index)
        {
            index = Math.Clamp(index, 0, _slides.Length - 1);
            if (index == _index) return;
            int dir = index > _index ? 1 : -1;
            bool firstShow = _index < 0;
            _index = index;
            bool motion = SystemParameters.ClientAreaAnimation && !firstShow;

            var slide = new DoubleAnimation(-index * PageW, TimeSpan.FromMilliseconds(motion ? 560 : 0))
            {
                EasingFunction = new SpringEase { Damping = 0.86, Frequency = 7.5 },
            };
            _shift.BeginAnimation(TranslateTransform.XProperty, slide);

            // A página que entra: o conteúdo sobe de leve e aparece.
            var page = _pages[index];
            if (SystemParameters.ClientAreaAnimation)
            {
                var lift = new TranslateTransform(0, 14);
                page.RenderTransform = lift;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                page.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
            }

            for (int i = 0; i < _dots.Children.Count; i++)
            {
                var dot = (Border)_dots.Children[i];
                bool on = i == index;
                dot.SetResourceReference(Border.BackgroundProperty, on ? "Accent" : "Line");
                dot.BeginAnimation(WidthProperty, new DoubleAnimation(on ? 20 : 7, TimeSpan.FromMilliseconds(motion ? 300 : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            _back.Visibility = index == 0 ? Visibility.Hidden : Visibility.Visible;
            _next.Content = index == _slides.Length - 1 ? L.T("Começar a usar") : L.T("Continuar");
        }

        private Button MakeButton(bool primary)
        {
            const string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
                "<Grid><Border x:Name='Bg' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='12' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<Border x:Name='Hover' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='12' Background='{TemplateBinding Foreground}' Opacity='0' IsHitTestVisible='False'/></Grid>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Hover' Property='Opacity' Value='0.08'/></Trigger>" +
                "<Trigger Property='IsPressed' Value='True'><Setter TargetName='Hover' Property='Opacity' Value='0.16'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
            var b = new Button
            {
                Template = (ControlTemplate)XamlReader.Parse(xaml),
                Padding = primary ? new Thickness(24, 10, 24, 10) : new Thickness(16, 10, 16, 10),
                FontSize = 14, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, Focusable = false,
                Background = Brushes.Transparent,
            };
            if (primary)
            {
                b.SetResourceReference(BackgroundProperty, "Accent");
                b.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
            }
            else
            {
                b.SetResourceReference(ForegroundProperty, "Text2");
            }
            return b;
        }

        private static Color ParseColor(string? hex)
        {
            try { if (ColorConverter.ConvertFromString(hex ?? "") is Color c) return c; } catch { }
            return Color.FromRgb(0x7C, 0xE3, 0xA1);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
