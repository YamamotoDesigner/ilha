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

        /// <summary>Verifica atualizações agora e devolve a mensagem para mostrar.</summary>
        public Func<System.Threading.Tasks.Task<string>>? CheckForUpdates { get; set; }

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            MonitorCombo.ItemsSource = Forms.Screen.AllScreens.Select((s, i) => new
            {
                Label = $"Monitor {i + 1}{(s.Primary ? " (principal)" : "")} — {s.Bounds.Width}×{s.Bounds.Height}",
                Device = s.DeviceName,
            }).ToList();
            _settings.MonitorDevice ??= Forms.Screen.PrimaryScreen?.DeviceName;

            AddSwatches(IslandSwatches, IslandColorBox, IslandPresets);
            AddSwatches(AccentSwatches, AccentColorBox, AccentPresets);

            StartupCheck.IsChecked = Startup.IsEnabled;
            VersionText.Text = $"Versão {UpdateService.CurrentVersion}";
            AboutVersion.Text = $"Versão {UpdateService.CurrentVersion}";
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
            AttachedRadio.IsChecked = !floating;
            FloatingRadio.IsChecked = floating;
            bool top = string.Equals(_settings.TabPosition, "Top", StringComparison.OrdinalIgnoreCase);
            SideTabsRadio.IsChecked = !top;
            TopTabsRadio.IsChecked = top;
            UpdateFloatOptions();
            _syncing = false;
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
            _settings.IslandStyle = sender == FloatingRadio ? "Floating" : "Attached";
            UpdateFloatOptions();
        }

        private void Tabs_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || _syncing) return;
            _settings.TabPosition = sender == TopTabsRadio ? "Top" : "Side";
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
            _settings.FloatWidth = null;
            _settings.FloatHeight = null;
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

        private void UpdateMaterialHint() => MaterialHint.Text = IsGlass
            ? "Vidro: o que está atrás da ilha aparece desfocado, tingido pela cor escolhida."
            : "Sólido: a ilha usa a cor escolhida, sem transparência.";

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
            UpdateStatus.Text = "Verificando…";
            UpdateStatus.Text = await CheckForUpdates();
            CheckUpdatesButton.IsEnabled = true;
        }
    }
}
