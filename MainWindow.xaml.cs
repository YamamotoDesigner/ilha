using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Ilha
{
    public partial class MainWindow : Window
    {
        private const double BaseWindowWidth = 640, BaseWindowHeight = 250;
        private const double ExpandedWidth = 580, ExpandedHeight = 210;
        private const double CollapsedExtraWidth = 60;
        private const int HotkeyId = 0x4948;
        private static readonly string GlyphPlay = ((char)0xE768).ToString();
        private static readonly string GlyphPause = ((char)0xE769).ToString();

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly MediaService _media = new();
        private readonly ObservableCollection<ShelfItem> _shelf = new();
        private readonly ObservableCollection<ClipItem> _clips = new();
        private readonly DispatcherTimer _expandTimer = new();
        private readonly DispatcherTimer _collapseTimer = new();
        private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(1.5) };
        private readonly DispatcherTimer _notesSave = new() { Interval = TimeSpan.FromMilliseconds(600) };
        private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
        private readonly UpdateService _updates = new();
        private UpdateService.UpdateInfo? _availableUpdate;
        private bool _installingUpdate;

        private IntPtr _hwnd;
        private IntPtr _previousForeground;
        private TrayIcon? _tray;
        private SettingsWindow? _settingsWindow;
        private bool _expanded, _pinned, _draggingOut, _hiddenForFullscreen, _barsAnimating;
        private bool _hotkeyRegistered, _clipboardListening, _ignoreNextClipboard;
        private string _collapsedKey = "";
        private Point _dragStart;
        private ShelfItem? _pressedItem;

        // Timer
        private TimeSpan _timerPreset = TimeSpan.FromMinutes(5);
        private TimeSpan _timerPaused = TimeSpan.FromMinutes(5);
        private DateTime? _timerEndsAt;
        private bool _timerAlerting;
        private DateTime _timerAlertUntil;
        private int _alertSoundsLeft;

        private bool TimerRunning => _timerEndsAt != null;

        private TimeSpan TimerRemaining
        {
            get
            {
                if (_timerEndsAt is not DateTime end) return _timerPaused;
                var r = end - DateTime.Now;
                return r < TimeSpan.Zero ? TimeSpan.Zero : r;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            foreach (var path in _settings.Shelf)
            {
                var item = ShelfItem.From(path);
                if (item.Exists()) _shelf.Add(item);
            }
            ShelfItems.ItemsSource = _shelf;
            _shelf.CollectionChanged += (_, _) =>
            {
                UpdateShelfHint();
                _settings.Shelf = _shelf.Select(s => s.Path).ToList();
                _settings.Save();
            };
            UpdateShelfHint();

            ClipboardItems.ItemsSource = _clips;
            _clips.CollectionChanged += (_, _) =>
                ClipboardEmpty.Visibility = _clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            NotesBox.Text = _settings.Notes;
            UpdateNotesPlaceholder();

            _expandTimer.Tick += (_, _) =>
            {
                _expandTimer.Stop();
                if (Island.IsMouseOver) Expand();
            };
            _collapseTimer.Tick += (_, _) =>
            {
                _collapseTimer.Stop();
                if (!ShouldStayOpen()) Collapse();
            };
            _notesSave.Tick += (_, _) =>
            {
                _notesSave.Stop();
                _settings.Notes = NotesBox.Text;
                _settings.Save();
            };
            _tick.Tick += (_, _) => OnTick();
            _watch.Tick += (_, _) => WatchFullscreen();
            _media.Changed += UpdateMedia;

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Deactivated += (_, _) => RestartCollapseTimer();
            DpiChanged += (_, _) => Dispatcher.BeginInvoke(PositionOnScreen, DispatcherPriority.Background);
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) =>
                Dispatcher.BeginInvoke(PositionOnScreen, DispatcherPriority.Background);
        }

        // ───────────── Janela ─────────────

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(_hwnd);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            _tray = new TrayIcon(OpenSettings, ToggleFromHotkey, ExitApp);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplySettings();
            UpdateMedia();
            UpdateTimerUi();
            _tick.Start();
            _watch.Start();
            _updateTimer.Tick += (_, _) => AutoCheckUpdates();
            _updateTimer.Start();
            var firstCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) }; // não atrasa a abertura
            firstCheck.Tick += (_, _) =>
            {
                firstCheck.Stop();
                AutoCheckUpdates();
            };
            firstCheck.Start();
            try
            {
                await _media.InitAsync();
            }
            catch
            {
                // Sem acesso à API de mídia: o resto do app continua funcionando.
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                ToggleFromHotkey();
                handled = true;
            }
            else if (msg == Native.WM_CLIPBOARDUPDATE)
            {
                Dispatcher.BeginInvoke(ReadClipboard, DispatcherPriority.Background);
            }
            return IntPtr.Zero;
        }

        private Forms.Screen TargetScreen() =>
            Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.MonitorDevice)
            ?? Forms.Screen.PrimaryScreen
            ?? Forms.Screen.AllScreens[0];

        /// <summary>Centraliza no topo do monitor escolhido (coordenadas físicas, ciente de DPI).</summary>
        private void PositionOnScreen()
        {
            if (_hwnd == IntPtr.Zero) return;
            var bounds = TargetScreen().Bounds;
            Native.MoveTopmost(_hwnd, bounds.Left, bounds.Top); // aplica o DPI do monitor primeiro
            var dpi = VisualTreeHelper.GetDpi(this);
            int widthPx = (int)Math.Round(Width * dpi.DpiScaleX);
            Native.MoveTopmost(_hwnd, bounds.Left + (bounds.Width - widthPx) / 2, bounds.Top);
        }

        private void WatchFullscreen()
        {
            if (_hwnd == IntPtr.Zero) return;
            bool hide = _settings.HideOnFullscreen && !_timerAlerting &&
                        Native.IsForegroundFullscreen(TargetScreen().Bounds, _hwnd);
            if (hide != _hiddenForFullscreen)
            {
                _hiddenForFullscreen = hide;
                if (hide)
                {
                    Hide();
                }
                else
                {
                    Show();
                    PositionOnScreen();
                }
            }
            else if (!hide)
            {
                Native.MakeTopmost(_hwnd); // outros apps "topmost" podem ter passado na frente
            }
        }

        private void ExitApp()
        {
            _notesSave.Stop();
            _settings.Notes = NotesBox.Text;
            _settings.Save();
            if (_hotkeyRegistered) Native.UnregisterHotKey(_hwnd, HotkeyId);
            if (_clipboardListening) Native.RemoveClipboardFormatListener(_hwnd);
            _tray?.Dispose();
            _tray = null;
            Application.Current.Shutdown();
        }

        // ───────────── Atualizações ─────────────

        /// <summary>Consulta o GitHub; em caso de erro de rede, só ignora (tenta de novo mais tarde).</summary>
        private async System.Threading.Tasks.Task<bool> CheckForUpdatesAsync(bool notify)
        {
            if (_installingUpdate) return _availableUpdate != null;
            var info = await _updates.CheckAsync();
            bool isNew = info != null && info.Version != _availableUpdate?.Version;
            _availableUpdate = info;

            if (info == null)
            {
                UpdateButton.Visibility = Visibility.Collapsed;
                _tray?.SetUpdate(null, null, notify: false);
                return false;
            }

            UpdateButton.Content = $"Atualizar para {info.Version}";
            UpdateButton.ToolTip = "Baixa e instala a nova versão; o Ilha reabre sozinho.";
            UpdateButton.IsEnabled = true;
            UpdateButton.Visibility = Visibility.Visible;
            _tray?.SetUpdate($"Instalar atualização {info.Version}", () => _ = InstallUpdateAsync(), notify && isNew);
            return true;
        }

        private async void AutoCheckUpdates()
        {
            if (!_settings.AutoCheckUpdates) return;
            try
            {
                await CheckForUpdatesAsync(notify: true);
            }
            catch
            {
                // Sem internet ou GitHub fora do ar: tenta de novo no próximo ciclo.
            }
        }

        private async System.Threading.Tasks.Task<string> CheckUpdatesFromSettingsAsync()
        {
            try
            {
                if (!await CheckForUpdatesAsync(notify: false))
                    return $"Você já está na versão mais recente ({UpdateService.CurrentVersion}).";
                _ = InstallUpdateAsync();
                return $"Baixando a versão {_availableUpdate!.Version}… o Ilha vai reabrir sozinho.";
            }
            catch (Exception ex)
            {
                return "Não foi possível verificar: " + ex.Message;
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e) => await InstallUpdateAsync();

        private async System.Threading.Tasks.Task InstallUpdateAsync()
        {
            if (_installingUpdate || _availableUpdate is not { } info) return;
            _installingUpdate = true;
            UpdateButton.IsEnabled = false;
            UpdateButton.Visibility = Visibility.Visible;
            try
            {
                var progress = new Progress<double>(p => UpdateButton.Content = $"Baixando… {p:P0}");
                string installer = await _updates.DownloadAsync(info, progress);
                UpdateButton.Content = "Instalando…";
                UpdateService.LaunchInstaller(installer);
                ExitApp(); // o instalador substitui o exe e reabre o Ilha
            }
            catch (Exception ex)
            {
                _installingUpdate = false;
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = "Tentar atualizar de novo";
                UpdateButton.ToolTip = "Falhou: " + ex.Message;
            }
        }

        // ───────────── Configurações ─────────────

        private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

        private void OpenSettings()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }
            _settingsWindow = new SettingsWindow(_settings);
            _settingsWindow.SettingsChanged += ApplySettings;
            _settingsWindow.QuitRequested += ExitApp;
            _settingsWindow.CheckForUpdates = CheckUpdatesFromSettingsAsync;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
            Collapse();
        }

        private void ApplySettings()
        {
            var s = _settings;

            // Cores
            Island.Background = new SolidColorBrush(ParseColor(s.IslandColor, Colors.Black));
            Resources["Accent"] = new SolidColorBrush(ParseColor(s.AccentColor, Color.FromRgb(0x7C, 0xE3, 0xA1)));

            // Forma
            double r = Math.Clamp(s.CornerRadius, 0, 40);
            Island.CornerRadius = new CornerRadius(0, 0, r, r);
            Island.Effect = s.ShowShadow ? new DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.4 } : null;
            CollapsedView.Height = s.CollapsedHeight;
            MiniArt.Width = MiniArt.Height = Math.Max(12, Math.Min(22, s.CollapsedHeight - 10));

            // Tamanho geral
            double scale = Math.Clamp(s.Scale, 0.6, 1.6);
            RootScale.ScaleX = RootScale.ScaleY = scale;
            Width = BaseWindowWidth * scale;
            Height = BaseWindowHeight * scale;

            // Tempos
            _expandTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, s.HoverDelayMs));
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, s.CollapseDelayMs));

            // Página inicial
            bool media = s.ShowMedia, shelf = s.ShowShelf;
            MediaArea.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
            ShelfZone.Visibility = shelf ? Visibility.Visible : Visibility.Collapsed;
            HomeMediaCol.Width = media ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            HomeGapCol.Width = new GridLength(media && shelf ? 16 : 0);
            HomeShelfCol.Width = !shelf ? new GridLength(0)
                : media ? new GridLength(200) : new GridLength(1, GridUnitType.Star);

            // Abas
            TabHome.Visibility = media || shelf ? Visibility.Visible : Visibility.Collapsed;
            TabNotes.Visibility = s.ShowNotes ? Visibility.Visible : Visibility.Collapsed;
            TabTimer.Visibility = s.ShowTimer ? Visibility.Visible : Visibility.Collapsed;
            TabClipboard.Visibility = s.ShowClipboard ? Visibility.Visible : Visibility.Collapsed;
            var visibleTabs = new[] { TabHome, TabNotes, TabTimer, TabClipboard }
                .Where(t => t.Visibility == Visibility.Visible).ToList();
            TabBar.Visibility = visibleTabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (visibleTabs.Count == 0)
                SelectTab("home");
            else if (!visibleTabs.Any(t => t.IsChecked == true))
                visibleTabs[0].IsChecked = true;

            // Atalho global e área de transferência
            if (_hwnd != IntPtr.Zero)
            {
                if (s.HotkeyEnabled && !_hotkeyRegistered)
                    _hotkeyRegistered = Native.RegisterHotKey(_hwnd, HotkeyId, 0x0002 | 0x0001 | 0x4000, 0x4E); // Ctrl+Alt+N
                else if (!s.HotkeyEnabled && _hotkeyRegistered)
                    _hotkeyRegistered = !Native.UnregisterHotKey(_hwnd, HotkeyId);

                if (s.ShowClipboard && !_clipboardListening)
                    _clipboardListening = Native.AddClipboardFormatListener(_hwnd);
                else if (!s.ShowClipboard && _clipboardListening)
                {
                    Native.RemoveClipboardFormatListener(_hwnd);
                    _clipboardListening = false;
                    _clips.Clear();
                }
            }

            UpdateClock();
            PositionOnScreen();
            UpdateCollapsedState(force: true);
        }

        private static Color ParseColor(string? text, Color fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(text) && ColorConverter.ConvertFromString(text.Trim()) is Color c)
                    return c;
            }
            catch
            {
                // Valor inválido digitado: mantém a cor padrão.
            }
            return fallback;
        }

        // ───────────── Expandir / recolher ─────────────

        private bool ShouldStayOpen() =>
            _draggingOut || _pinned || _timerAlerting || Island.IsMouseOver ||
            (IsForeground && NotesBox.IsKeyboardFocused);

        /// <summary>
        /// Se a ilha é de fato a janela em primeiro plano. Window.IsActive do WPF não serve aqui:
        /// com WS_EX_NOACTIVATE ele pode ficar "true" sem a janela ter o foco real.
        /// </summary>
        private bool IsForeground => _hwnd != IntPtr.Zero && Native.ForegroundWindow() == _hwnd;

        private void Island_MouseEnter(object sender, MouseEventArgs e)
        {
            _collapseTimer.Stop();
            if (_settings.HoverDelayMs <= 0) Expand();
            else _expandTimer.Start();
        }

        private void Island_MouseLeave(object sender, MouseEventArgs e)
        {
            _expandTimer.Stop();
            _pinned = false;
            RestartCollapseTimer();
        }

        private void RestartCollapseTimer()
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }

        private void ToggleFromHotkey()
        {
            if (_expanded)
            {
                _pinned = false;
                Collapse();
            }
            else
            {
                if (_hiddenForFullscreen) return;
                _pinned = true;
                Expand();
            }
        }

        private void Expand()
        {
            _collapseTimer.Stop();
            if (_expanded) return;
            _expanded = true;

            AnimateSize(ExpandedWidth, ExpandedHeight, _settings.SpringAnimation);
            Fade(CollapsedView, 0, 120);
            Fade(ExpandedView, 1, 220, delayMs: 90);
            ExpandedView.IsHitTestVisible = true;
            UpdateClock();
            UpdateBattery();
            UpdateProgress();
        }

        private void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            _pinned = false;
            ExpandedView.IsHitTestVisible = false;
            Fade(ExpandedView, 0, 120);
            ReleaseKeyboard();
            UpdateCollapsedState(force: true);
        }

        private void UpdateCollapsedState(bool force = false)
        {
            var s = _settings;
            bool playing = _media.HasSession && _media.IsPlaying;
            bool showTimer = (TimerRunning || _timerAlerting) && s.CollapsedShowTimer && s.ShowTimer;
            bool showArt = playing && s.CollapsedShowArt && s.ShowMedia;
            bool showBars = playing && s.CollapsedShowEqualizer && s.ShowMedia && !showTimer;
            bool any = showArt || showTimer || showBars;

            MiniArt.Visibility = showArt ? Visibility.Visible : Visibility.Collapsed;
            Bars.Visibility = showBars ? Visibility.Visible : Visibility.Collapsed;
            CollapsedTimerText.Visibility = showTimer ? Visibility.Visible : Visibility.Collapsed;
            SetBarsAnimating(showBars);

            string key = $"{any}|{s.CollapsedWidth}|{s.CollapsedHeight}";
            if (_expanded || (!force && key == _collapsedKey)) return;
            _collapsedKey = key;

            AnimateSize(s.CollapsedWidth + (any ? CollapsedExtraWidth : 0), s.CollapsedHeight, spring: false);
            Fade(CollapsedView, any ? 1 : 0, 200, delayMs: any ? 120 : 0);
        }

        private void AnimateSize(double width, double height, bool spring)
        {
            IEasingFunction ease = spring
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                : new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(spring ? 380 : 260);
            Island.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = ease });
        }

        private static void Fade(UIElement element, double to, int ms, int delayMs = 0) =>
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
            });

        private void SetBarsAnimating(bool on)
        {
            if (on == _barsAnimating) return;
            _barsAnimating = on;
            var bars = new Rectangle[] { Bar1, Bar2, Bar3 };
            double[] periods = { 0.42, 0.58, 0.36 };
            for (int i = 0; i < bars.Length; i++)
            {
                var scale = (ScaleTransform)bars[i].RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, on
                    ? new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(periods[i]))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase(),
                    }
                    : null);
            }
        }

        // ───────────── Abas ─────────────

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string tab }) SelectTab(tab);
        }

        private void SelectTab(string tab)
        {
            if (PageHome == null) return; // durante InitializeComponent
            PageHome.Visibility = tab == "home" ? Visibility.Visible : Visibility.Collapsed;
            PageNotes.Visibility = tab == "notes" ? Visibility.Visible : Visibility.Collapsed;
            PageTimer.Visibility = tab == "timer" ? Visibility.Visible : Visibility.Collapsed;
            PageClipboard.Visibility = tab == "clipboard" ? Visibility.Visible : Visibility.Collapsed;
            if (tab != "notes") ReleaseKeyboard();
        }

        // ───────────── Relógio, bateria, progresso ─────────────

        private void OnTick()
        {
            UpdateTimer();
            if (!_expanded) return;
            UpdateClock();
            UpdateBattery();
            UpdateProgress();
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            string format = (_settings.Use24h ? "HH:mm" : "h:mm") + (_settings.ShowSeconds ? ":ss" : "") +
                            (_settings.Use24h ? "" : " tt");
            ClockText.Text = now.ToString(format, CultureInfo.CurrentCulture);
            DateText.Text = now.ToString("ddd, d MMM", CultureInfo.CurrentCulture);
        }

        private void UpdateBattery()
        {
            var power = Forms.SystemInformation.PowerStatus;
            if (power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery) ||
                power.BatteryChargeStatus == Forms.BatteryChargeStatus.Unknown)
            {
                BatteryPanel.Visibility = Visibility.Collapsed;
                return;
            }

            BatteryPanel.Visibility = Visibility.Visible;
            int percent = (int)Math.Round(power.BatteryLifePercent * 100);
            bool charging = power.PowerLineStatus == Forms.PowerLineStatus.Online;
            BatteryText.Text = $"{percent}%";
            ChargingIcon.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
            BatteryText.Foreground = percent <= 20 && !charging
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B))
                : Brushes.White;
        }

        private void UpdateProgress()
        {
            var duration = _media.Duration;
            if (!_media.HasSession || duration <= TimeSpan.Zero)
            {
                ProgressRow.Visibility = Visibility.Hidden;
                return;
            }

            ProgressRow.Visibility = Visibility.Visible;
            var position = _media.Position;
            double ratio = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
            ProgressFill.Width = ProgressTrack.ActualWidth * ratio;
            PositionText.Text = FormatTime(position);
            DurationText.Text = FormatTime(duration);
        }

        private static string FormatTime(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        // ───────────── Mídia ─────────────

        private void UpdateMedia()
        {
            bool has = _media.HasSession && (_media.Title.Length > 0 || _media.IsPlaying);
            MediaDetails.Visibility = has ? Visibility.Visible : Visibility.Hidden;
            NoMediaPanel.Visibility = has ? Visibility.Collapsed : Visibility.Visible;

            TitleText.Text = string.IsNullOrEmpty(_media.Title) ? "Sem título" : _media.Title;
            ArtistText.Text = _media.Artist;
            SourceText.Text = _media.SourceApp;
            ArtBrush.ImageSource = _media.Art;
            MiniArtBrush.ImageSource = _media.Art;
            ArtFallback.Visibility = _media.Art == null ? Visibility.Visible : Visibility.Collapsed;
            PlayPauseButton.Content = _media.IsPlaying ? GlyphPause : GlyphPlay;

            UpdateCollapsedState();
            if (_expanded) UpdateProgress();
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();
        private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
        private async void Previous_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();

        // ───────────── Prateleira ─────────────

        private void UpdateShelfHint()
        {
            bool empty = _shelf.Count == 0;
            ShelfHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ClearShelfButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Island_DragOver(object sender, DragEventArgs e)
        {
            bool accept = !_draggingOut && _settings.ShowShelf && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            if (!accept) return;
            _collapseTimer.Stop();
            TabHome.IsChecked = true;
            Expand();
        }

        private void Island_DragLeave(object sender, DragEventArgs e)
        {
            if (!_draggingOut) RestartCollapseTimer();
        }

        private void Island_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (_draggingOut || !_settings.ShowShelf || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            foreach (var path in paths)
            {
                if (_shelf.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                var item = ShelfItem.From(path);
                if (item.Exists()) _shelf.Add(item);
            }
        }

        private void ShelfItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(this);
            _pressedItem = (sender as FrameworkElement)?.DataContext as ShelfItem;
            if (e.ClickCount == 2 && _pressedItem != null)
            {
                OpenPath(_pressedItem.Path);
                _pressedItem = null;
                e.Handled = true;
            }
        }

        private void ShelfItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (_pressedItem == null || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var item = _pressedItem;
            _pressedItem = null;
            _draggingOut = true;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
                DragDrop.DoDragDrop((DependencyObject)sender, data,
                    DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            finally
            {
                _draggingOut = false;
            }

            if (!item.Exists()) _shelf.Remove(item); // foi movido para outro lugar
            RestartCollapseTimer();
        }

        private void ShelfRemove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ShelfItem item) _shelf.Remove(item);
            _pressedItem = null;
        }

        private void ClearShelf_Click(object sender, RoutedEventArgs e) => _shelf.Clear();

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
                // Arquivo removido ou sem app associado.
            }
        }

        // ───────────── Notas ─────────────

        private void NotesArea_MouseEnter(object sender, MouseEventArgs e)
        {
            // Lembra o app em uso para devolver o foco quando sair das notas.
            if (!IsForeground) _previousForeground = Native.ForegroundWindow();
            Native.SetNoActivate(_hwnd, false);
        }

        private void NotesArea_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!NotesBox.IsKeyboardFocusWithin) Native.SetNoActivate(_hwnd, true);
        }

        private void NotesBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // O clique já ativou a janela (WS_EX_NOACTIVATE está desligado sobre as notas);
            // aqui só garantimos o foco do teclado na caixa.
            if (!NotesBox.IsKeyboardFocusWithin) NotesBox.Focus();
        }

        private void NotesBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            ReleaseKeyboard();
            RestartCollapseTimer();
        }

        private void NotesBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateNotesPlaceholder();
            _notesSave.Stop();
            _notesSave.Start();
        }

        private void UpdateNotesPlaceholder() =>
            NotesPlaceholder.Visibility = NotesBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sai da edição das notas e devolve o foco ao app que estava em uso antes.</summary>
        private void ReleaseKeyboard()
        {
            if (_hwnd == IntPtr.Zero) return;
            if (NotesBox.IsKeyboardFocusWithin) Keyboard.ClearFocus();
            if (IsForeground && _previousForeground != IntPtr.Zero && Native.IsWindow(_previousForeground))
                Native.SetForegroundWindow(_previousForeground);
            _previousForeground = IntPtr.Zero;
            if (!PageNotes.IsMouseOver) Native.SetNoActivate(_hwnd, true);
        }

        // ───────────── Timer ─────────────

        private void TimerPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int minutes)) return;
            StopAlert();
            _timerPreset = TimeSpan.FromMinutes(minutes);
            _timerPaused = _timerPreset;
            _timerEndsAt = DateTime.Now + _timerPreset;
            UpdateTimerUi();
        }

        private void TimerAdd_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int minutes)) return;
            StopAlert();
            var delta = TimeSpan.FromMinutes(minutes);
            if (_timerEndsAt is DateTime end)
            {
                var newEnd = end + delta;
                _timerEndsAt = newEnd < DateTime.Now ? DateTime.Now : newEnd;
            }
            else
            {
                _timerPaused += delta;
                if (_timerPaused < TimeSpan.Zero) _timerPaused = TimeSpan.Zero;
            }
            UpdateTimerUi();
        }

        private void TimerStartPause_Click(object sender, RoutedEventArgs e)
        {
            StopAlert();
            if (TimerRunning)
            {
                _timerPaused = TimerRemaining;
                _timerEndsAt = null;
            }
            else
            {
                if (_timerPaused <= TimeSpan.Zero) _timerPaused = _timerPreset;
                _timerEndsAt = DateTime.Now + _timerPaused;
            }
            UpdateTimerUi();
        }

        private void TimerReset_Click(object sender, RoutedEventArgs e)
        {
            StopAlert();
            _timerEndsAt = null;
            _timerPaused = _timerPreset;
            UpdateTimerUi();
        }

        private void UpdateTimer()
        {
            if (TimerRunning && TimerRemaining <= TimeSpan.Zero)
            {
                _timerEndsAt = null;
                _timerPaused = TimeSpan.Zero;
                StartAlert();
            }

            if (_timerAlerting)
            {
                if (_alertSoundsLeft > 0 && (DateTime.Now.Millisecond < 250))
                {
                    _alertSoundsLeft--;
                    SystemSounds.Exclamation.Play();
                }
                if (DateTime.Now >= _timerAlertUntil)
                {
                    StopAlert();
                    RestartCollapseTimer();
                }
            }

            if (TimerRunning || _timerAlerting) UpdateTimerUi();
        }

        private void UpdateTimerUi()
        {
            var remaining = TimeSpan.FromSeconds(Math.Ceiling(TimerRemaining.TotalSeconds));
            string text = FormatTime(remaining);
            TimerDisplay.Text = text;
            CollapsedTimerText.Text = text;
            TimerStartButton.Content = TimerRunning ? GlyphPause : GlyphPlay;
            TimerStatus.Text = _timerAlerting ? "Tempo esgotado!"
                : TimerRunning ? "Em andamento"
                : remaining < _timerPreset && remaining > TimeSpan.Zero ? "Pausado"
                : "Pronto";
            TimerStatus.Foreground = _timerAlerting ? (Brush)Resources["Accent"] : Brushes.White;
            UpdateCollapsedState();
        }

        private void StartAlert()
        {
            _timerAlerting = true;
            _timerAlertUntil = DateTime.Now.AddSeconds(12);
            _alertSoundsLeft = 3;
            SystemSounds.Exclamation.Play();

            if (_hiddenForFullscreen)
            {
                _hiddenForFullscreen = false;
                Show();
                PositionOnScreen();
            }
            if (_settings.ShowTimer) TabTimer.IsChecked = true;
            Expand();
            TimerDisplay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(450))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
            UpdateTimerUi();
        }

        private void StopAlert()
        {
            if (!_timerAlerting) return;
            _timerAlerting = false;
            _alertSoundsLeft = 0;
            TimerDisplay.BeginAnimation(OpacityProperty, null);
            UpdateTimerUi();
        }

        // ───────────── Área de transferência ─────────────

        private void ReadClipboard()
        {
            if (_ignoreNextClipboard)
            {
                _ignoreNextClipboard = false;
                return;
            }

            string? text = null;
            for (int attempt = 0; attempt < 3 && text == null; attempt++)
            {
                try
                {
                    if (!Clipboard.ContainsText()) return;
                    text = Clipboard.GetText();
                }
                catch
                {
                    // Outro app está segurando a área de transferência; tenta de novo.
                    System.Threading.Thread.Sleep(40);
                }
            }
            if (string.IsNullOrWhiteSpace(text)) return;

            var existing = _clips.FirstOrDefault(c => c.Text == text);
            if (existing != null) _clips.Remove(existing);
            _clips.Insert(0, new ClipItem(text));
            while (_clips.Count > Math.Max(1, _settings.ClipboardMax)) _clips.RemoveAt(_clips.Count - 1);
        }

        private void ClipItem_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ClipItem item) return;
            try
            {
                _ignoreNextClipboard = true;
                Clipboard.SetText(item.Text);
                _clips.Remove(item);
                _clips.Insert(0, item);
                ClipboardStatus.Text = "Copiado! ✓";
            }
            catch
            {
                _ignoreNextClipboard = false;
                ClipboardStatus.Text = "Não foi possível copiar agora";
            }

            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            reset.Tick += (_, _) =>
            {
                reset.Stop();
                ClipboardStatus.Text = "Clique em um item para copiar de novo";
            };
            reset.Start();
        }

        private void ClipboardClear_Click(object sender, RoutedEventArgs e) => _clips.Clear();
    }
}
