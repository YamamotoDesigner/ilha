using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Ilha
{
    public partial class SettingsWindow : Window
    {
        private static readonly string[] IslandPresets =
            { "#000000", "#1C1C1E", "#2B2D42", "#14213D", "#3A0CA3", "#5C1A1A", "#0B3D2E" };

        private static readonly string[] AccentPresets =
            { "#7CE3A1", "#4CC9F0", "#A78BFA", "#F472B6", "#FB923C", "#FACC15", "#FFFFFF" };

        private readonly AppSettings _settings;
        private readonly DispatcherTimer _apply = new() { Interval = TimeSpan.FromMilliseconds(120) };

        public event Action? SettingsChanged;
        public event Action? QuitRequested;
        public event Action? RestartRequested;
        public event Action? OnboardingRequested;

        /// <summary>Verifica atualizações agora e devolve a mensagem para mostrar.</summary>
        public Func<System.Threading.Tasks.Task<string>>? CheckForUpdates { get; set; }

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            ThemePalette.ApplyTo(this);
            L.ApplyTo(this);

            MonitorCombo.ItemsSource = Forms.Screen.AllScreens.Select((s, i) => new
            {
                Label = L.F("Monitor {0}", i + 1) + (s.Primary ? L.T(" (principal)") : "") + $" — {s.Bounds.Width}×{s.Bounds.Height}",
                Device = s.DeviceName,
            }).ToList();
            _settings.MonitorDevice ??= Forms.Screen.PrimaryScreen?.DeviceName;

            AddSwatches(IslandSwatches, IslandColorBox, IslandPresets);
            AddSwatches(AccentSwatches, AccentColorBox, AccentPresets);

            StartupCheck.IsChecked = Startup.IsEnabled;
            VersionText.Text = L.F("Versão {0}", UpdateService.CurrentVersion);
            AboutVersion.Text = L.F("Versão {0}", UpdateService.CurrentVersion);
            SyncMaterial();
            SyncChoices();
            ApplyAccent();
            ShowPage(PageGeneral);
            DataContext = _settings;

            _apply.Tick += (_, _) =>
            {
                _apply.Stop();
                _settings.Save();
                ApplyAccent();
                SettingsChanged?.Invoke();
            };
            AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((_, _) => DrawSizePreview()));
            SizeCanvas.SizeChanged += (_, _) => DrawSizePreview();

            // Qualquer alteração aplica ao vivo (com pequeno atraso para agrupar mudanças).
            AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((_, _) => ScheduleApply()));
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => ScheduleApply()));
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => ScheduleApply()));
            AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => ScheduleApply()));
            AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => ScheduleApply()));
        }

        private void ScheduleApply()
        {
            if (!IsLoaded) return;
            _apply.Stop();
            _apply.Start();
        }

        private static void AddSwatches(Panel host, TextBox target, string[] colors)
        {
            foreach (var hex in colors)
            {
                var swatch = new Button
                {
                    Style = (Style)host.FindResource("Swatch"),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    ToolTip = hex,
                };
                swatch.Click += (_, _) => target.Text = hex;
                host.Children.Add(swatch);
            }
        }

        private void StartupCheck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Startup.Set(StartupCheck.IsChecked == true);
            }
            catch
            {
                StartupCheck.IsChecked = Startup.IsEnabled;
            }
        }

        // ───────────── Navegação ─────────────

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            var page = sender == NavLook ? PageLook
                : sender == NavIsland ? PageIsland
                : sender == NavFeatures ? PageFeatures
                : sender == NavAlerts ? PageAlerts
                : sender == NavUpdates ? PageUpdates
                : sender == NavAbout ? PageAbout
                : PageGeneral;
            ShowPage(page);
            if (sender is RadioButton { Content: string title }) PageTitle.Text = title;
        }

        private void ShowPage(FrameworkElement page)
        {
            foreach (var p in new FrameworkElement[] { PageGeneral, PageLook, PageIsland, PageFeatures, PageAlerts, PageUpdates, PageAbout })
                p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
            PageSubtitle.Text = page.Tag as string ?? "";
            PageScroll.ScrollToTop();
            page.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>Abre direto na página de recursos (botão "+" da ilha).</summary>
        public void ShowFeatures() => NavFeatures.IsChecked = true;

        /// <summary>A cor de destaque das configurações acompanha a da ilha.</summary>
        private void ApplyAccent()
        {
            try
            {
                if (ColorConverter.ConvertFromString(_settings.AccentColor) is Color c)
                    Resources["Accent"] = new SolidColorBrush(c);
            }
            catch
            {
                // Cor inválida sendo digitada: mantém a anterior.
            }
        }

        // ───────────── Escolhas com cartão ─────────────

        private bool _syncing;

        private void SyncChoices()
        {
            _syncing = true;
            bool floating = string.Equals(_settings.IslandStyle, "Floating", StringComparison.OrdinalIgnoreCase);
            bool topFloat = string.Equals(_settings.IslandStyle, "TopFloating", StringComparison.OrdinalIgnoreCase);
            AttachedRadio.IsChecked = !floating && !topFloat;
            TopFloatRadio.IsChecked = topFloat;
            FloatingRadio.IsChecked = floating;
            bool top = string.Equals(_settings.TabPosition, "Top", StringComparison.OrdinalIgnoreCase);
            SideTabsRadio.IsChecked = !top;
            TopTabsRadio.IsChecked = top;
            bool full = string.Equals(_settings.OpenLayout, "Full", StringComparison.OrdinalIgnoreCase);
            TabsLayoutRadio.IsChecked = !full;
            FullLayoutRadio.IsChecked = full;
            bool light = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
            DarkRadio.IsChecked = !light;
            LightRadio.IsChecked = light;
            LangPt.IsChecked = !L.English;
            LangEn.IsChecked = L.English;
            UpdateFloatOptions();
            _syncing = false;
            DrawSizePreview();
        }

        // ───────────── Tema e idioma ─────────────

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || _syncing) return;
            bool light = sender == LightRadio;
            SetTheme(_settings, light);
            IslandColorBox.Text = _settings.IslandColor;
            ThemePalette.Apply(!light);
            ThemePalette.ApplyTo(this);
        }

        /// <summary>
        /// Muda o tema e, se a ilha estiver na cor padrão do tema anterior, troca para a do novo
        /// (preta no escuro, clara no claro). Cores escolhidas pelo usuário são mantidas.
        /// </summary>
        public static void SetTheme(AppSettings s, bool light)
        {
            s.Theme = light ? "Light" : "Dark";
            const string darkIsland = "#000000", lightIsland = "#F2F2F5";
            if (light && string.Equals(s.IslandColor, darkIsland, StringComparison.OrdinalIgnoreCase)) s.IslandColor = lightIsland;
            if (!light && string.Equals(s.IslandColor, lightIsland, StringComparison.OrdinalIgnoreCase)) s.IslandColor = darkIsland;
        }

        private void Lang_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || _syncing) return;
            string lang = sender == LangEn ? "en" : "pt";
            _settings.Language = lang;
            _settings.Save();
            RestartRow.Visibility = lang != L.Lang ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Restart_Click(object sender, RoutedEventArgs e) => RestartRequested?.Invoke();

        private void Onboarding_Click(object sender, RoutedEventArgs e) => OnboardingRequested?.Invoke();

        // ───────────── Prévia do tamanho ─────────────

        /// <summary>
        /// Desenha o topo da tela com a ilha fechada (cheia) e o contorno de até onde ela abre,
        /// na mesma proporção entre si — para ver o tamanho antes de abrir.
        /// </summary>
        private void DrawSizePreview()
        {
            if (SizeCanvas == null || SizeCanvas.ActualWidth <= 0) return;
            var s = _settings;
            double cw = SizeCanvas.ActualWidth, ch = SizeCanvas.ActualHeight;
            double openW = OWidthSlider.Value, openH = OHeightSlider.Value;
            double closedW = CWidthSlider.Value, closedH = CHeightSlider.Value;
            if (openW <= 0 || openH <= 0) return; // antes dos valores chegarem
            double scale = Math.Min((cw - 24) / openW, (ch - 18) / openH) * 0.92;
            bool detached = !string.Equals(s.IslandStyle, "Attached", StringComparison.OrdinalIgnoreCase);
            double top = detached ? 8 : 0;
            double r = RadiusSlider.Value * scale;

            SizeCanvas.Children.Clear();
            var accent = (Brush)FindResource("Accent");
            var outline = new Border
            {
                Width = openW * scale,
                Height = openH * scale,
                CornerRadius = detached ? new CornerRadius(Math.Min(r, openH * scale * 0.4)) : new CornerRadius(0, 0, r, r),
                BorderBrush = (Brush)FindResource("Text2"),
                BorderThickness = detached ? new Thickness(1.5) : new Thickness(1.5, 0, 1.5, 1.5),
                Background = (Brush)FindResource("Ink_08"),
                Opacity = 0.9,
            };
            Canvas.SetLeft(outline, (cw - outline.Width) / 2);
            Canvas.SetTop(outline, top);
            SizeCanvas.Children.Add(outline);

            double cr = Math.Min(r, closedH * scale * 0.4);
            var closed = new Border
            {
                Width = Math.Max(4, closedW * scale),
                Height = Math.Max(3, closedH * scale),
                CornerRadius = detached ? new CornerRadius(cr) : new CornerRadius(0, 0, cr, cr),
                Background = accent,
            };
            Canvas.SetLeft(closed, (cw - closed.Width) / 2);
            Canvas.SetTop(closed, top);
            SizeCanvas.Children.Add(closed);

            SizeClosedText.Text = L.F("Fechada {0} × {1}", Math.Round(closedW), Math.Round(closedH));
            SizeOpenText.Text = L.F("Aberta {0} × {1}", Math.Round(openW), Math.Round(openH));
        }

        private void UpdateFloatOptions()
        {
            bool floating = FloatingRadio.IsChecked == true;
            FloatOptions.Visibility = floating ? Visibility.Visible : Visibility.Collapsed;
            FloatHint.Visibility = FloatOptions.Visibility;
        }

        private void IslandStyle_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || _syncing) return;
            _settings.IslandStyle = sender == FloatingRadio ? "Floating" : sender == TopFloatRadio ? "TopFloating" : "Attached";
            UpdateFloatOptions();
            DrawSizePreview();
        }

        private void Tabs_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || _syncing) return;
            _settings.TabPosition = sender == TopTabsRadio ? "Top" : "Side";
        }

        /// <summary>Abre uma página das configurações (General, Look, Island, Features, Alerts, Updates, About).</summary>
        public void ShowPage(string name)
        {
            if (FindName("Nav" + name) is System.Windows.Controls.RadioButton nav) nav.IsChecked = true;
        }

        private void Layout_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || _syncing) return;
            _settings.OpenLayout = sender == FullLayoutRadio ? "Full" : "Tabs";
        }

        private void CenterIsland_Click(object sender, RoutedEventArgs e)
        {
            _settings.FloatCenterX = null;
            _settings.FloatTop = null;
            _apply.Stop();
            _settings.Save();
            SettingsChanged?.Invoke();
        }

        private void DefaultSize_Click(object sender, RoutedEventArgs e)
        {
            var d = new AppSettings();
            _settings.OpenWidth = d.OpenWidth;
            _settings.OpenHeight = d.OpenHeight;
            _settings.CollapsedWidth = d.CollapsedWidth;
            _settings.CollapsedHeight = d.CollapsedHeight;
            DataContext = null;
            DataContext = _settings;
            _apply.Stop();
            _settings.Save();
            SettingsChanged?.Invoke();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _settings.ResetPreferences();
            SyncMaterial();
            SyncChoices();
            DataContext = null;
            DataContext = _settings;
            _apply.Stop();
            _settings.Save();
            SettingsChanged?.Invoke();
        }

        private bool IsGlass => string.Equals(_settings.Material, "Glass", StringComparison.OrdinalIgnoreCase);

        private void SyncMaterial()
        {
            GlassRadio.IsChecked = IsGlass;
            SolidRadio.IsChecked = !IsGlass;
            UpdateMaterialHint();
        }

        private void UpdateMaterialHint()
        {
            MaterialHint.Text = IsGlass
                ? L.T("Vidro: o que está atrás da ilha aparece desfocado, tingido pela cor escolhida.")
                : L.T("Sólido: a ilha usa a cor escolhida, sem transparência.");
            // A intensidade só vale para o vidro; no sólido a cor já é cheia.
            TintGroup.Visibility = IsGlass ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Material_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            _settings.Material = sender == SolidRadio ? "Solid" : "Glass";
            UpdateMaterialHint();
        }

        private void Quit_Click(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();

        private void Support_Click(object sender, RoutedEventArgs e) => SupportWindow.ShowSingle();

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (CheckForUpdates == null) return;
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatus.Text = L.T("Verificando…");
            UpdateStatus.Text = await CheckForUpdates();
            CheckUpdatesButton.IsEnabled = true;
        }
    }
}
