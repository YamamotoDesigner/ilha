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
            VersionText.Text = $"Versão instalada: {UpdateService.CurrentVersion}";
            SupportButton.Visibility = Support.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
            DataContext = _settings;

            _apply.Tick += (_, _) =>
            {
                _apply.Stop();
                _settings.Save();
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

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _settings.ResetPreferences();
            DataContext = null;
            DataContext = _settings;
            _apply.Stop();
            _settings.Save();
            SettingsChanged?.Invoke();
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
