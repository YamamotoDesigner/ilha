using System;
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
    /// "Como podemos te chamar?": aparece na primeira abertura (e para quem atualizou e ainda não
    /// tem nome). O nome é usado na saudação do dia. Sem nome, não segue.
    /// </summary>
    public sealed class NameWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly TextBox _box;
        private readonly Button _go;
        private bool _done;

        public event Action? Finished;

        public NameWindow(AppSettings settings)
        {
            _settings = settings;
            Title = "Ilha";
            Width = 460;
            Height = 400;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            SetResourceReference(FontFamilyProperty, "UiFont");
            SetResourceReference(BackgroundProperty, "WinBg");
            SetResourceReference(ForegroundProperty, "WinFg");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 40, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
            });
            ThemePalette.ApplyTo(this);
            var accent = Color.FromRgb(0x7C, 0xE3, 0xA1);
            try { if (ColorConverter.ConvertFromString(settings.AccentColor) is Color c) accent = c; } catch { }
            Resources["Accent"] = new SolidColorBrush(accent);
            SourceInitialized += (_, _) =>
            {
                int round = 2;
                try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref round, sizeof(int)); } catch { }
            };

            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(44, 10, 44, 0) };
            var face = new Border { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, Child = new Viewbox { Child = NudgeArt.Build("hello") } };
            col.Children.Add(face);
            col.Children.Add(new TextBlock
            {
                Text = L.T("Como podemos te chamar?"), FontSize = 24, FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 22, 0, 0),
            });
            var sub = new TextBlock
            {
                Text = L.T("A Ilha vai te cumprimentar pelo nome todo dia."), FontSize = 14, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
            };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Text2");
            col.Children.Add(sub);

            _box = new TextBox
            {
                FontSize = 17, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 24, 0, 0),
                BorderThickness = new Thickness(1.5), MaxLength = 30, TextAlignment = TextAlignment.Center,
                Template = (ControlTemplate)XamlReader.Parse(
                    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TextBox'>" +
                    "<Border CornerRadius='12' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>" +
                    "<ScrollViewer x:Name='PART_ContentHost' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/></Border></ControlTemplate>"),
            };
            _box.SetResourceReference(BackgroundProperty, "CardBg");
            _box.SetResourceReference(BorderBrushProperty, "Line");
            _box.SetResourceReference(ForegroundProperty, "WinFg");
            _box.SetResourceReference(TextBox.CaretBrushProperty, "WinFg");
            _box.GotKeyboardFocus += (_, _) => _box.SetResourceReference(BorderBrushProperty, "Accent");
            _box.LostKeyboardFocus += (_, _) => _box.SetResourceReference(BorderBrushProperty, "Line");
            _box.Text = settings.UserName;
            col.Children.Add(_box);

            const string btn =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
                "<Border x:Name='Bg' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='12' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>" +
                "<ContentPresenter HorizontalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Bg' Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
            _go = new Button
            {
                Content = L.T("Continuar"), Template = (ControlTemplate)XamlReader.Parse(btn), Padding = new Thickness(0, 11, 0, 11),
                Margin = new Thickness(0, 14, 0, 0), FontSize = 15, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), IsEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            _go.SetResourceReference(BackgroundProperty, "Accent");
            _go.Click += (_, _) => Finish();
            col.Children.Add(_go);
            Content = col;

            _box.TextChanged += (_, _) => _go.IsEnabled = _box.Text.Trim().Length > 0;
            _go.IsEnabled = _box.Text.Trim().Length > 0;
            _box.KeyDown += (_, e) => { if (e.Key == Key.Enter && _go.IsEnabled) Finish(); };
            // Sem nome não segue: fechar a janela não pula esta etapa.
            Closing += (_, e) => { if (!_done) e.Cancel = true; };
            Loaded += (_, _) =>
            {
                _box.Focus();
                Activate();
                if (SystemParameters.ClientAreaAnimation)
                {
                    var move = new TranslateTransform(0, 16);
                    col.RenderTransform = move;
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    col.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
                    move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(560)) { EasingFunction = ease });
                }
            };
        }

        private void Finish()
        {
            string name = _box.Text.Trim();
            if (name.Length == 0) return;
            _settings.UserName = name;
            _settings.Save();
            _done = true;
            Close();
            Finished?.Invoke();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
