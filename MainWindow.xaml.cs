using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
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
        private const double BaseWindowWidth = 720, BaseWindowHeight = 300;
        private const double ExpandedWidth = 660, ExpandedHeight = 244;
        // Ilha flutuante redimensionável: limites do tamanho aberto.
        private const double MinFloatWidth = 340, MaxFloatWidth = 1040, MinFloatHeight = 130, MaxFloatHeight = 460;
        private double _expW = ExpandedWidth, _expH = ExpandedHeight;
        private const double CollapsedExtraWidth = 64;
        private const double PeekExtraWidth = 170;
        private const int HotkeyId = 0x4948;
        private Geometry GlyphPlay => (Geometry)FindResource("IcoPlay");
        private Geometry GlyphPause => (Geometry)FindResource("IcoPause");

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly MediaService _media = new();
        private readonly CameraService _camera = new();
        private readonly VolumeMonitor _volume = new();
        private readonly DispatcherTimer _peekTimer = new();
        private bool _peeking;
        private BackdropWindow? _glassWindow;
        private float? _lastVolume;
        private bool? _lastMuted;
        private bool? _lastCharging;
        private string _lastTrackKey = "";
        private DateTime _ignoreTrackUntil = DateTime.Now.AddSeconds(4);
        private DateTime _calendarDate;
        private int _monthOffset;
        private double _cornerRadius = 22;
        private readonly SystemStats _stats = new();
        private readonly ObservableCollection<TodoItem> _todos = new();
        private string _currentTab = "home";
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

        /// <param name="glass">Janela do vidro, já aberta; vira dona desta para ficar sempre logo atrás.</param>
        public MainWindow(BackdropWindow? glass)
        {
            InitializeComponent();
#if DEBUG
            // Testes visuais: muda só na memória (as configurações reais não são tocadas).
            if (Environment.GetEnvironmentVariable("ILHA_TEST_FLOAT") == "1") _settings.IslandStyle = "Floating";
            if (Environment.GetEnvironmentVariable("ILHA_TEST_STYLE") is { Length: > 0 } testStyle) _settings.IslandStyle = testStyle;
            if (Environment.GetEnvironmentVariable("ILHA_TEST_TABSTOP") == "1") _settings.TabPosition = "Top";
            if (double.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_FW"), out double tfw)) _settings.FloatWidth = tfw;
            if (double.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_FH"), out double tfh)) _settings.FloatHeight = tfh;
#endif
            _glassWindow = glass;
            Owner = glass;

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

            foreach (var todo in _settings.Todos) _todos.Add(todo);
            TodoItems.ItemsSource = _todos;
            _todos.CollectionChanged += (_, _) => SaveTodos();
            UpdateTodoSummary();
            _stats.Sample();

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
            _peekTimer.Tick += (_, _) => EndPeek();
            _camera.FrameSourceChanged += image =>
            {
                MirrorBrush.ImageSource = image;
                MirrorPlaceholder.Visibility = Visibility.Collapsed;
            };
            Island.SizeChanged += (_, _) =>
            {
                UpdateIslandCorners();
                UpdateGlassRegion();
            };
            LocationChanged += (_, _) => _glassWindow?.Follow(_hwnd); // inclusive enquanto arrasta
            CalGrid.SizeChanged += (_, _) => SizeCalendarCells();
            CpuGraph.SizeChanged += (_, _) => UpdateStats();
            MemGraph.SizeChanged += (_, _) => UpdateStats();
            DrawTimerTrack();

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
            SizeChanged += (_, _) => _glassWindow?.Follow(_hwnd);
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
#if DEBUG
            // Captura de tela de desenvolvimento: abre a ilha já fixada numa aba.
            if (Environment.GetEnvironmentVariable("ILHA_TEST_TAB") is { Length: > 0 } testTab)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    if (testTab is "settings" or "support")
                    {
                        Window w;
                        if (testTab == "support")
                        {
                            SupportWindow.ShowSingle();
                            w = Application.Current.Windows.OfType<SupportWindow>().First();
                        }
                        else
                        {
                            OpenSettings();
                            w = _settingsWindow!;
                            w.Height = 900;
                        }
                        if (Environment.GetEnvironmentVariable("ILHA_TEST_PAGE") is { Length: > 0 } navName &&
                            w.FindName(navName) is RadioButton nav)
                            nav.IsChecked = true;
                        var snap = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                        snap.Tick += (_, _) =>
                        {
                            snap.Stop();
                            var root = (FrameworkElement)w.Content;
                            var dpi = VisualTreeHelper.GetDpi(w);
                            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                                (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
                                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                            var dv = new DrawingVisual();
                            using (var dc = dv.RenderOpen())
                            {
                                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null,
                                    new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                            }
                            bmp.Render(dv);
                            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                            using var fs = System.IO.File.Create(Environment.GetEnvironmentVariable("ILHA_TEST_SHOT") ?? "settings.png");
                            enc.Save(fs);
                        };
                        snap.Start();
                        return;
                    }
                    if (testTab == "pip")
                    {
                        // Vídeo público curto, sem som; e registra o que o localizador acha nos navegadores.
                        PipWindow.TestMute = true;
                        string? found = YouTubeLocator.FromBrowsers(_media.Title);
                        System.IO.File.WriteAllText(Environment.GetEnvironmentVariable("ILHA_TEST_SHOT") ?? "pip.txt",
                                                    $"browser={found ?? "(nenhum)"}");
                        PipWindow.Open("jNQXAC9IVRw", 3, "Me at the zoo");
                        return;
                    }
                    if (testTab == "peek")
                    {
                        ShowPeek((Geometry)FindResource("IcoSpeaker"), true, (Geometry)FindResource("IcoWaves"),
                                 null, "Volume", 0.62, "62", seconds: 30);
                        return;
                    }
                    _pinned = true;
                    Expand();
                    foreach (var tabButton in TabBar.Children.OfType<RadioButton>())
                        if ((string)tabButton.Tag == testTab) tabButton.IsChecked = true;
                };
                t.Start();
            }
#endif
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

        /// <summary>Flutuante livre: arrasta, redimensiona e lembra a posição.</summary>
        private bool Floating => string.Equals(_settings.IslandStyle, "Floating", StringComparison.OrdinalIgnoreCase);

        /// <summary>Solta da borda (flutuante ou "solta no topo"): quatro cantos arredondados, espaço e sombra.</summary>
        private bool Detached => Floating ||
                                 string.Equals(_settings.IslandStyle, "TopFloating", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Posição salva da ilha flutuante (centro horizontal, topo), se ainda estiver dentro de
        /// algum monitor (ex.: o monitor pode ter sido desconectado).
        /// </summary>
        private System.Drawing.Point? SavedFloatPosition()
        {
            if (!Floating || _settings.FloatCenterX is not int x || _settings.FloatTop is not int y) return null;
            var p = new System.Drawing.Point(x, y);
            var probe = new System.Drawing.Point(x, y + 10);
            return Forms.Screen.AllScreens.Any(s => s.Bounds.Contains(probe)) ? p : null;
        }

        private Forms.Screen TargetScreen()
        {
            if (SavedFloatPosition() is { } p) return Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y + 10));
            return Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.MonitorDevice)
                   ?? Forms.Screen.PrimaryScreen
                   ?? Forms.Screen.AllScreens[0];
        }

        /// <summary>
        /// Colada: centralizada no topo do monitor escolhido. Flutuante: onde o usuário deixou
        /// (ou centralizada um pouco abaixo do topo). Coordenadas físicas, ciente de DPI.
        /// </summary>
        private void PositionOnScreen()
        {
            if (_hwnd == IntPtr.Zero) return;
            var screen = TargetScreen();
            var bounds = screen.Bounds;
            Native.MoveTopmost(_hwnd, bounds.Left, bounds.Top); // aplica o DPI do monitor primeiro
            var dpi = VisualTreeHelper.GetDpi(this);
            int widthPx = (int)Math.Round(Width * dpi.DpiScaleX);
            int left = bounds.Left + (bounds.Width - widthPx) / 2;
            int top = bounds.Top + (Detached ? (int)Math.Round(10 * dpi.DpiScaleY) : 0);
            if (SavedFloatPosition() is { } saved)
            {
                left = saved.X - widthPx / 2;
                top = saved.Y;
            }
#if DEBUG
            if (int.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_Y"), out int testY)) top += testY;
#endif
            Native.MoveTopmost(_hwnd, left, top);
            _glassWindow?.Follow(_hwnd);
            UpdateGlassRegion();
        }

        /// <summary>Cantos proporcionais à altura (nunca vira pílula); flutuante arredonda os quatro.</summary>
        private void UpdateIslandCorners()
        {
            double r = Math.Min(_cornerRadius, Island.ActualHeight * 0.4);
            Island.CornerRadius = Detached ? new CornerRadius(r) : new CornerRadius(0, 0, r, r);
            // O canto de redimensionar fica dentro da curva (o ponto a 45° do arco fica a ~0,29·r da borda).
            double inset = Math.Min(_cornerRadius, ExpandedView.Height * 0.4) * 0.29 + 3;
            ResizeGrip.Margin = new Thickness(0, 0, inset, inset);
        }

        // ───────────── Ilha flutuante: arrastar e fixar ─────────────

        private void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_expanded) DragIsland(e); // recolhida: qualquer ponto da ilha serve de alça
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragIsland(e);

        private void DragIsland(MouseButtonEventArgs e)
        {
            if (!Floating || _settings.FloatLocked || e.ButtonState != MouseButtonState.Pressed) return;
            e.Handled = true;
            _collapseTimer.Stop();
            _draggingOut = true; // mantém aberta durante o arraste
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // Botão já solto.
            }
            finally
            {
                _draggingOut = false;
            }
            if (GetWindowRect(_hwnd, out var r))
            {
                _settings.FloatCenterX = (r.Left + r.Right) / 2;
                _settings.FloatTop = r.Top;
                _settings.Save();
            }
            _glassWindow?.Follow(_hwnd);
            if (!Island.IsMouseOver) RestartCollapseTimer();
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            _settings.FloatLocked = !_settings.FloatLocked;
            _settings.Save();
            UpdatePinUi();
        }

        private void UpdatePinUi()
        {
            bool floating = Floating;
            PinButton.Visibility = floating ? Visibility.Visible : Visibility.Collapsed;
            GripGlyph.Visibility = floating && !_settings.FloatLocked ? Visibility.Visible : Visibility.Collapsed;
            PinGlyph.Filled = _settings.FloatLocked;
            PinButton.Foreground = _settings.FloatLocked ? (Brush)Resources["Accent"] : new SolidColorBrush(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF));
            PinButton.ToolTip = _settings.FloatLocked ? "Posição fixada — clique para soltar e poder arrastar"
                                                      : "Fixar a ilha neste lugar";
            HeaderBar.Cursor = floating && !_settings.FloatLocked ? Cursors.SizeAll : null;
            ResizeGrip.Visibility = floating && !_settings.FloatLocked ? Visibility.Visible : Visibility.Collapsed;
        }

        // Redimensionar pelo canto: a ilha cresce para os dois lados (fica centralizada) e para baixo.
        private bool _resizing;
        private Point _resizeStart;
        private double _resizeW0, _resizeH0;

        private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!Floating || _settings.FloatLocked || !_expanded) return;
            e.Handled = true;
            _resizing = true;
            _draggingOut = true; // não recolhe durante o gesto
            _collapseTimer.Stop();
            _resizeStart = PointToScreen(e.GetPosition(this));
            _resizeW0 = _expW;
            _resizeH0 = _expH;
            ResizeGrip.CaptureMouse();
        }

        private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_resizing) return;
            var p = PointToScreen(e.GetPosition(this));
            var dpi = VisualTreeHelper.GetDpi(this);
            double dx = (p.X - _resizeStart.X) / (dpi.DpiScaleX * RootScale.ScaleX);
            double dy = (p.Y - _resizeStart.Y) / (dpi.DpiScaleY * RootScale.ScaleY);
            _expW = Math.Clamp(_resizeW0 + 2 * dx, MinFloatWidth, MaxFloatWidth);
            _expH = Math.Clamp(_resizeH0 + dy, MinFloatHeight, MaxFloatHeight);
            ExpandedView.Width = _expW;
            ExpandedView.Height = _expH;
            UpdateResponsiveLayout();
            Island.BeginAnimation(WidthProperty, null);
            Island.BeginAnimation(HeightProperty, null);
            Island.Width = _expW;
            Island.Height = _expH;
        }

        private void ResizeGrip_MouseUp(object sender, MouseButtonEventArgs e) => ResizeGrip.ReleaseMouseCapture();

        private void ResizeGrip_LostCapture(object sender, MouseEventArgs e)
        {
            if (!_resizing) return;
            _resizing = false;
            _draggingOut = false;
            _settings.FloatWidth = Math.Round(_expW);
            _settings.FloatHeight = Math.Round(_expH);
            _settings.Save();
            if (!Island.IsMouseOver) RestartCollapseTimer();
        }

        /// <summary>Abas no trilho lateral ou numa barra no topo do cabeçalho.</summary>
        private void ApplyTabPosition()
        {
            bool top = string.Equals(_settings.TabPosition, "Top", StringComparison.OrdinalIgnoreCase);
            var currentHost = TabBar.Parent as FrameworkElement;
            if (top && currentHost != TopTabsHost)
            {
                RailHost.Children.Remove(TabBar);
                TopTabsHost.Child = TabBar;
            }
            else if (!top && currentHost != RailHost)
            {
                TopTabsHost.Child = null;
                RailHost.Children.Add(TabBar);
            }
            TabBar.Orientation = top ? Orientation.Horizontal : Orientation.Vertical;
            foreach (var tab in TabBar.Children.OfType<RadioButton>())
            {
                tab.Width = top ? 34 : 36;
                tab.Height = top ? 24 : 30; // na lateral, UpdateResponsiveLayout ajusta à altura disponível
                tab.Margin = top ? new Thickness(1, 0, 1, 0) : new Thickness(0, 0, 0, 3);
            }
            HeaderRow.Height = new GridLength(top ? 30 : 26); // a barra de abas tem 30 de altura
            RailCol.Width = new GridLength(top ? 0 : 36);
            RailGapCol.Width = new GridLength(top ? 0 : 14);
            RailLine.Visibility = top ? Visibility.Collapsed : Visibility.Visible;
            TopTabsHost.Visibility = top && TabBar.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        }

        // Abaixo deste tamanho de conteúdo, tudo encolhe por igual em vez de cortar.
        private const double ContentMinWidth = 420, ContentMinHeight = 150;
        private const double ContentPadX = 34, ContentPadY = 30;

        private bool TabsOnTop => string.Equals(_settings.TabPosition, "Top", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Encaixa o conteúdo no tamanho atual da ilha aberta: escala quando fica pequeno demais e
        /// esconde o que não cabe (sistema, prateleira, tarefas, modos do timer, data...).
        /// </summary>
        private void UpdateResponsiveLayout()
        {
            var s = _settings;
            double cw = _expW - ContentPadX, ch = _expH - ContentPadY;
            double k = Math.Min(1, Math.Min(cw / ContentMinWidth, ch / ContentMinHeight));
            ContentScale.ScaleX = ContentScale.ScaleY = k;
            double vw = cw / k, vh = ch / k; // tamanho "virtual" em que o layout é montado
            ContentRoot.Width = vw;
            ContentRoot.Height = vh;

            bool top = TabsOnTop;
            double pageW = vw - (top ? 0 : 50);
            double pageH = vh - (top ? 30 : 26) - 10;
            var visible = Visibility.Visible;
            var hidden = Visibility.Collapsed;

            // Trilho lateral: as abas dividem a altura disponível, sem cortar a última.
            if (!top)
            {
                var tabs = TabBar.Children.OfType<RadioButton>().Where(t => t.Visibility == visible).ToList();
                double tabH = tabs.Count == 0 ? 30 : Math.Clamp(vh / tabs.Count - 3, 20, 30);
                foreach (var t in tabs) t.Height = tabH;
            }

            // Cabeçalho
            DateText.Visibility = (top ? vw >= 520 : pageW >= 330) ? visible : hidden;
            BatteryText.Visibility = !top || vw >= 480 ? visible : hidden;

            // Início: a mídia ocupa o espaço livre; sistema e prateleira só aparecem se couberem
            bool media = s.ShowMedia;
            double used = media ? 210 : 0;
            bool shelf = s.ShowShelf && pageW >= used + (media ? 14 : 0) + 140;
            if (shelf) used += (media ? 14 : 0) + 150;
            bool stats = s.ShowStats && pageH >= 116 && pageW >= used + 12 + 118;
            MediaArea.Visibility = media ? visible : hidden;
            ShelfZone.Visibility = shelf ? visible : hidden;
            StatsCard.Visibility = stats ? visible : hidden;
            var star = new GridLength(1, GridUnitType.Star);
            HomeMediaCol.Width = media ? star : new GridLength(0);
            HomeStatsCol.Width = !stats ? new GridLength(0) : media || shelf ? new GridLength(118) : star;
            HomeShelfCol.Width = !shelf ? new GridLength(0) : media ? new GridLength(stats ? 150 : 190) : star;
            HomeGap1.Width = new GridLength(media && (stats || shelf) ? 14 : 0);
            HomeGap2.Width = new GridLength(stats && shelf ? 12 : 0);
            // Pouca altura: capa menor e sem a linha do app de origem
            bool compactMedia = pageH < 136;
            ArtBox.Width = ArtBox.Height = compactMedia ? 42 : 64;
            ArtBox.CornerRadius = new CornerRadius(compactMedia ? 10 : 13);
            SourceText.Visibility = compactMedia ? hidden : visible;

            // Agenda: a lista de tarefas sai primeiro; o mês fica
            bool todo = pageW >= 500 && pageH >= 118;
            TodoPanel.Visibility = todo ? visible : hidden;
            AgendaTodoCol.Width = new GridLength(todo ? 230 : 0);
            AgendaGapCol.Width = new GridLength(todo ? 14 : 0);

            // Foco: o quadrado encolhe com a altura; modos e atalhos só se couber
            TimerModes.Visibility = pageW >= 480 ? visible : hidden;
            TimerChips.Visibility = pageH >= 140 && pageW >= 380 ? visible : hidden;

            // Espelho
            MirrorFrame.Width = Math.Min(300, pageW);

            UpdateStats();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect32 { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

        /// <summary>
        /// Acompanha o formato atual da ilha (quadro a quadro durante a animação) com o vidro de trás.
        /// </summary>
        private void UpdateGlassRegion()
        {
            if (_hwnd == IntPtr.Zero || _glassWindow == null) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            double scale = RootScale.ScaleX;
            double w = Island.ActualWidth * scale, h = Island.ActualHeight * scale;
            double left = (ActualWidth - w) / 2;
            double radius = Island.CornerRadius.BottomLeft * scale;
            _glassWindow.SetShape((float)(left * dpi.DpiScaleX), (float)(w * dpi.DpiScaleX),
                                  (float)(h * dpi.DpiScaleY), (float)(radius * dpi.DpiScaleX), Detached);
        }

        private void ApplyMaterial()
        {
            var s = _settings;
            var baseColor = ParseColor(s.IslandColor, Colors.Black);
            bool glass = string.Equals(s.Material, "Glass", StringComparison.OrdinalIgnoreCase);
            if (_glassWindow != null) _glassWindow.GlassVisible = glass;
            bool blurred = glass && _glassWindow?.Supported == true;

            if (glass)
            {
                // Vidro: a cor escolhida tinge o desfoque; mais escura embaixo, para o texto ler bem.
                double tint = Math.Clamp(s.GlassTint, 0, 100) / 100.0;
                double alpha = blurred ? tint : 0.55 + tint * 0.45; // sem desfoque, fica quase sólido
                byte top = (byte)Math.Round(255 * Math.Clamp(alpha, 0, 1));
                byte bottom = (byte)Math.Round(255 * Math.Clamp(alpha + 0.12, 0, 1));
                IslandFill.Background = new LinearGradientBrush(
                    Color.FromArgb(top, baseColor.R, baseColor.G, baseColor.B),
                    Color.FromArgb(bottom, baseColor.R, baseColor.G, baseColor.B), 90);
                IslandSheen.Visibility = Visibility.Visible;
                IslandRim.Visibility = Visibility.Visible;
                // Solta na tela, uma sombra leve dá a sensação de estar "flutuando".
                Island.Effect = Detached && s.ShowShadow
                    ? new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.3 }
                    : null;
            }
            else
            {
                IslandFill.Background = new SolidColorBrush(baseColor);
                IslandSheen.Visibility = Visibility.Collapsed;
                IslandRim.Visibility = baseColor.R + baseColor.G + baseColor.B < 60 ? Visibility.Collapsed : Visibility.Visible;
                Island.Effect = s.ShowShadow ? new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Direction = 270, Opacity = 0.45 } : null;
            }
            UpdateGlassRegion();
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
                    _glassWindow?.Hide();
                }
                else
                {
                    _glassWindow?.Show();
                    Show();
                    PositionOnScreen();
                }
            }
            else if (!hide)
            {
                // Outros apps "topmost" podem ter passado na frente: vidro primeiro, ilha por cima.
                if (_glassWindow != null) Native.MakeTopmost(new WindowInteropHelper(_glassWindow).Handle);
                Native.MakeTopmost(_hwnd);
            }
        }

        private void ExitApp()
        {
            _notesSave.Stop();
            _settings.Notes = NotesBox.Text;
            _settings.Save();
            if (_hotkeyRegistered) Native.UnregisterHotKey(_hwnd, HotkeyId);
            if (_clipboardListening) Native.RemoveClipboardFormatListener(_hwnd);
            _camera.Stop();
            PipWindow.CloseIfOpen();
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

            // Cores e material
            Resources["Accent"] = new SolidColorBrush(ParseColor(s.AccentColor, Color.FromRgb(0x7C, 0xE3, 0xA1)));

            // Forma
            _cornerRadius = Math.Clamp(s.CornerRadius, 0, 40);
            UpdateIslandCorners();
            IslandRim.BorderThickness = Detached ? new Thickness(1) : new Thickness(1, 0, 1, 1);
            UpdatePinUi();
            CollapsedView.Height = s.CollapsedHeight;
            PeekView.Height = s.CollapsedHeight;
            MiniArt.Width = MiniArt.Height = Math.Max(12, Math.Min(22, s.CollapsedHeight - 10));
            PeekArt.Width = PeekArt.Height = MiniArt.Width;

            // Tamanho geral
            double scale = Math.Clamp(s.Scale, 0.6, 1.6);
            RootScale.ScaleX = RootScale.ScaleY = scale;
            // Flutuante: tamanho escolhido pelo usuário, e a janela (transparente) já comporta o máximo,
            // para redimensionar sem mover a ilha.
            bool floating = Floating;
            _expW = floating ? Math.Clamp(s.FloatWidth ?? ExpandedWidth, MinFloatWidth, MaxFloatWidth) : ExpandedWidth;
            _expH = floating ? Math.Clamp(s.FloatHeight ?? ExpandedHeight, MinFloatHeight, MaxFloatHeight) : ExpandedHeight;
            ExpandedView.Width = _expW;
            ExpandedView.Height = _expH;
            Width = (floating ? MaxFloatWidth + 60 : BaseWindowWidth) * scale;
            Height = (floating ? MaxFloatHeight + 56 : BaseWindowHeight) * scale;
            if (_expanded) AnimateSize(_expW, _expH, spring: false);

            // Tempos
            _expandTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, s.HoverDelayMs));
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, s.CollapseDelayMs));

            PipButton.Visibility = s.EnablePip && _media.FromBrowser ? Visibility.Visible : Visibility.Collapsed;

            // Abas
            TabHome.Visibility = s.ShowMedia || s.ShowShelf || s.ShowStats ? Visibility.Visible : Visibility.Collapsed;
            TabAgenda.Visibility = s.ShowCalendar ? Visibility.Visible : Visibility.Collapsed;
            TabNotes.Visibility = s.ShowNotes ? Visibility.Visible : Visibility.Collapsed;
            TabTimer.Visibility = s.ShowTimer ? Visibility.Visible : Visibility.Collapsed;
            TabClipboard.Visibility = s.ShowClipboard ? Visibility.Visible : Visibility.Collapsed;
            TabMirror.Visibility = s.ShowMirror ? Visibility.Visible : Visibility.Collapsed;
            var visibleTabs = TabBar.Children.OfType<RadioButton>()
                .Where(t => t.Visibility == Visibility.Visible).ToList();
            TabBar.Visibility = visibleTabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            ApplyTabPosition();
            UpdateResponsiveLayout();
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
            UpdateCalendar(force: true);
            ApplyMaterial();
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
            (IsForeground && Keyboard.FocusedElement is TextBox);

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
            if (_peeking) EndPeek(animate: false);

            AnimateSize(_expW, _expH, _settings.SpringAnimation);
            Fade(CollapsedView, 0, 100);
            Fade(ExpandedView, 1, 260, delayMs: 80);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var grow = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(60) };
            ExpandedScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            ExpandedScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ExpandedShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(60) });
            ExpandedView.IsHitTestVisible = true;
            UpdateClock();
            UpdateCalendar();
            UpdateBattery();
            UpdateProgress();
            UpdateStats();
            if (_currentTab == "mirror") StartMirror();
        }

        private void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            _pinned = false;
            ExpandedView.IsHitTestVisible = false;
            Fade(ExpandedView, 0, 110);
            ReleaseKeyboard();
            StopMirror();
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
            SetBarsAnimating(showBars && !_expanded);

            string key = $"{any}|{_peeking}|{s.CollapsedWidth}|{s.CollapsedHeight}";
            if (_expanded || (!force && key == _collapsedKey)) return;
            _collapsedKey = key;

            double width = s.CollapsedWidth + (_peeking ? PeekExtraWidth : any ? CollapsedExtraWidth : 0);
            AnimateSize(width, s.CollapsedHeight, spring: _settings.SpringAnimation && _peeking);
            Fade(CollapsedView, any && !_peeking ? 1 : 0, 200, delayMs: any && !_peeking ? 120 : 0);
            Fade(PeekView, _peeking ? 1 : 0, _peeking ? 220 : 140, delayMs: _peeking ? 90 : 0);
        }

        private void AnimateSize(double width, double height, bool spring)
        {
            IEasingFunction ease = spring
                ? new SpringEase()
                : new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(spring ? 560 : 280);
            Island.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = ease });
        }

        private static void Fade(UIElement element, double to, int ms, int delayMs = 0) =>
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
            });

        // ───────────── Avisos rápidos (volume, carregando, nova música) ─────────────

        private void ShowPeek(Geometry? icon, bool filled, Geometry? icon2, ImageSource? art, string text,
                              double? level, string value, Brush? accent = null, double seconds = 1.8)
        {
            if (_expanded || _hiddenForFullscreen) return;
            PeekGlyph.Data = icon;
            PeekGlyph.Filled = filled;
            PeekGlyph2.Data = icon2;
            PeekGlyph.Foreground = PeekGlyph2.Foreground = accent ?? Brushes.White;
            PeekArtBrush.ImageSource = art;
            PeekArt.Visibility = art != null ? Visibility.Visible : Visibility.Collapsed;
            PeekGlyph.Visibility = PeekGlyph2.Visibility = art != null ? Visibility.Collapsed : Visibility.Visible;
            PeekText.Text = text;
            PeekValue.Text = value;
            PeekValue.Visibility = value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            PeekBar.Visibility = level.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (level is double l)
            {
                PeekBarFill.Background = accent ?? Brushes.White;
                PeekBarFill.BeginAnimation(WidthProperty, new DoubleAnimation(Math.Clamp(l, 0, 1) * 70, TimeSpan.FromMilliseconds(140)));
            }

            _peekTimer.Stop();
            _peekTimer.Interval = TimeSpan.FromSeconds(seconds);
            _peekTimer.Start();
            if (!_peeking)
            {
                _peeking = true;
                UpdateCollapsedState(force: true);
            }
        }

        private void EndPeek() => EndPeek(animate: true);

        private void EndPeek(bool animate)
        {
            _peekTimer.Stop();
            if (!_peeking) return;
            _peeking = false;
            if (animate) UpdateCollapsedState(force: true);
            else PeekView.BeginAnimation(OpacityProperty, null);
            if (!animate) PeekView.Opacity = 0;
        }

        private void WatchVolume()
        {
            var reading = _volume.Read(out bool deviceChanged);
            if (reading is not { } r)
            {
                _lastVolume = null;
                return;
            }
            var (level, muted) = r;
            bool changed = _lastVolume is float last && !deviceChanged &&
                           (Math.Abs(last - level) > 0.004 || _lastMuted != muted);
            _lastVolume = level;
            _lastMuted = muted;
            if (!changed || !_settings.LiveVolume) return;

            var speaker = (Geometry)FindResource("IcoSpeaker");
            var extra = (Geometry)FindResource(muted || level < 0.005 ? "IcoMute" : "IcoWaves");
            ShowPeek(speaker, true, extra, null, muted ? "Mudo" : "Volume", muted ? 0 : level,
                     muted ? "" : $"{Math.Round(level * 100)}");
        }

        private void WatchCharging()
        {
            var power = Forms.SystemInformation.PowerStatus;
            if (power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery)) return;
            bool charging = power.PowerLineStatus == Forms.PowerLineStatus.Online;
            bool changed = _lastCharging is bool was && was != charging;
            _lastCharging = charging;
            if (!changed || !_settings.LiveCharging) return;

            int percent = (int)Math.Round(power.BatteryLifePercent * 100);
            var green = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            ShowPeek((Geometry)FindResource("IcoBolt"), true, null, null,
                     charging ? "Carregando" : "Na bateria", power.BatteryLifePercent, $"{percent}%",
                     charging ? green : null, seconds: 2.6);
        }

        private void WatchTrack()
        {
            if (!_media.HasSession || string.IsNullOrEmpty(_media.Title)) return;
            string key = _media.Title + "\n" + _media.Artist;
            if (key == _lastTrackKey) return;
            bool first = _lastTrackKey.Length == 0;
            _lastTrackKey = key;
            if (first || DateTime.Now < _ignoreTrackUntil || !_media.IsPlaying || !_settings.LiveTrack || !_settings.ShowMedia) return;

            string text = string.IsNullOrEmpty(_media.Artist) ? _media.Title : $"{_media.Title} · {_media.Artist}";
            ShowPeek((Geometry)FindResource("IcoMusic"), false, null, _media.Art, text, null, "", seconds: 3.2);
        }

        private void SetBarsAnimating(bool on)
        {
            if (on == _barsAnimating) return;
            _barsAnimating = on;
            var bars = new Rectangle[] { Bar1, Bar2, Bar3, Bar4 };
            double[] periods = { 0.42, 0.58, 0.36, 0.5 };
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
            bool changed = tab != _currentTab;
            _currentTab = tab;
            var pages = new (string Tab, FrameworkElement Page)[]
            {
                ("home", PageHome), ("agenda", PageAgenda), ("notes", PageNotes), ("timer", PageTimer),
                ("clipboard", PageClipboard), ("mirror", PageMirror),
            };
            foreach (var (name, page) in pages)
            {
                bool show = name == tab;
                page.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show && changed)
                {
                    // Troca suave: a página nova entra com fade e um leve deslize.
                    page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
                    var shift = new TranslateTransform();
                    page.RenderTransform = shift;
                    shift.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
            }
            if (tab != "notes" && tab != "agenda") ReleaseKeyboard();
            if (tab == "agenda" && changed)
            {
                _monthOffset = 0;
                UpdateCalendar(force: true);
            }
            if (tab == "mirror" && _expanded) StartMirror();
            else StopMirror();
        }

        // ───────────── Espelho (câmera) ─────────────

        private async void StartMirror()
        {
            if (_camera.IsRunning) return;
            MirrorPlaceholder.Visibility = Visibility.Visible;
            MirrorRetry.Visibility = Visibility.Collapsed;
            MirrorStatus.Text = "Ligando a câmera…";
            MirrorBrush.ImageSource = null;
            string? error = await _camera.StartAsync();
            if (error == null)
            {
                if (!_expanded || _currentTab != "mirror") StopMirror(); // fechou enquanto ligava
                return;
            }
            MirrorStatus.Text = error;
            MirrorRetry.Visibility = Visibility.Visible;
        }

        private void StopMirror()
        {
            if (!_camera.IsRunning) return;
            _camera.Stop();
            MirrorBrush.ImageSource = null;
            MirrorPlaceholder.Visibility = Visibility.Visible;
            MirrorStatus.Text = "Ligando a câmera…";
        }

        private void MirrorRetry_Click(object sender, RoutedEventArgs e) => StartMirror();

        // ───────────── Relógio, bateria, calendário, progresso ─────────────

        private int _tickCount;

        private void OnTick()
        {
            UpdateTimer();
            WatchVolume();
            bool everySecond = ++_tickCount % 4 == 0;
            if (everySecond)
            {
                WatchCharging();
                _stats.Sample();
            }
            if (!_expanded) return;
            UpdateClock();
            UpdateCalendar();
            UpdateBattery();
            UpdateProgress();
            if (everySecond) UpdateStats();
        }

        /// <summary>Valores e gráficos de processador e memória.</summary>
        private void UpdateStats()
        {
            if (StatsCard.Visibility != Visibility.Visible) return;
            CpuValue.Text = $"{Math.Round(_stats.CpuNow * 100)}%";
            MemValue.Text = $"{Math.Round(_stats.MemoryNow * 100)}%";
            MemValue.ToolTip = $"{_stats.MemoryUsedGb:0.0} GB em uso";
            DrawSparkline(_stats.Cpu, CpuGraph, CpuLine, CpuArea);
            DrawSparkline(_stats.Memory, MemGraph, MemLine, MemArea);
        }

        private static void DrawSparkline(double[] values, FrameworkElement host, Path line, Path area)
        {
            double w = host.ActualWidth, h = host.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double step = w / (values.Length - 1);
            var points = new Point[values.Length];
            for (int i = 0; i < values.Length; i++)
                points[i] = new Point(i * step, 2 + (h - 3) * (1 - values[i]));

            var stroke = new StreamGeometry();
            using (var ctx = stroke.Open())
            {
                ctx.BeginFigure(points[0], false, false);
                ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
            }
            stroke.Freeze();
            line.Data = stroke;

            var fill = new StreamGeometry();
            using (var ctx = fill.Open())
            {
                ctx.BeginFigure(new Point(0, h), true, true);
                ctx.PolyLineTo(points.Append(new Point(w, h)).ToList(), false, false);
            }
            fill.Freeze();
            area.Data = fill;
        }

        private void UpdateCalendar(bool force = false)
        {
            var today = DateTime.Today;
            if (!force && today == _calendarDate) return;
            _calendarDate = today;
            var culture = CultureInfo.CurrentCulture;

            // Hoje (painel das tarefas)
            AgendaWeekday.Text = today.ToString("dddd", culture).ToUpper(culture);
            AgendaDate.Text = today.ToString("d 'de' MMMM", culture);

            // Mês exibido
            var month = new DateTime(today.Year, today.Month, 1).AddMonths(_monthOffset);
            CalMonth.Text = culture.TextInfo.ToTitleCase(month.ToString("MMMM yyyy", culture));
            int week = ISOWeek.GetWeekOfYear(_monthOffset == 0 ? today : month);
            CalWeekNumber.Text = $"SEMANA {week}";

            var firstDay = culture.DateTimeFormat.FirstDayOfWeek;
            CalHeader.Children.Clear();
            for (int i = 0; i < 7; i++)
            {
                var dow = (DayOfWeek)(((int)firstDay + i) % 7);
                CalHeader.Children.Add(new TextBlock
                {
                    Text = culture.DateTimeFormat.GetAbbreviatedDayName(dow).TrimEnd('.').Substring(0, 1).ToUpper(culture),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Resources["Tertiary"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }

            CalGrid.Children.Clear();
            int lead = ((int)month.DayOfWeek - (int)firstDay + 7) % 7;
            var start = month.AddDays(-lead);
            var accent = (Brush)Resources["Accent"];
            for (int i = 0; i < 42; i++)
            {
                var day = start.AddDays(i);
                bool inMonth = day.Month == month.Month;
                bool isToday = day == today;
                bool weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var cell = new Border
                {
                    Margin = new Thickness(3, 1, 3, 1),
                    CornerRadius = new CornerRadius(6),
                    Background = isToday ? accent : Brushes.Transparent,
                    // Com pouca altura, o número encolhe junto com a célula em vez de ser cortado.
                    Child = new Viewbox
                    {
                        StretchDirection = StretchDirection.DownOnly,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                    {
                        Text = day.Day.ToString(culture),
                        FontSize = 11.5,
                        FontWeight = isToday ? FontWeights.Bold : FontWeights.Medium,
                        Foreground = isToday ? new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11))
                            : !inMonth ? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))
                            : weekend ? (Brush)Resources["Tertiary"] : Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Typography = { NumeralAlignment = FontNumeralAlignment.Tabular },
                    } },
                };
                CalGrid.Children.Add(cell);
            }
            SizeCalendarCells();
        }

        /// <summary>Células do mês com largura proporcional à altura: o destaque de hoje nunca vira pílula.</summary>
        private void SizeCalendarCells()
        {
            double rowH = CalGrid.ActualHeight / 6, colW = CalGrid.ActualWidth / 7;
            if (rowH <= 0 || colW <= 0) return;
            double width = Math.Max(10, Math.Min(colW - 6, Math.Max(rowH - 2, 12) * 1.6));
            foreach (var cell in CalGrid.Children.OfType<Border>())
            {
                cell.Width = width;
                cell.HorizontalAlignment = HorizontalAlignment.Center;
                cell.CornerRadius = new CornerRadius(Math.Min(6, (rowH - 2) / 3));
            }
        }

        private void CalendarMonth_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int delta)) return;
            _monthOffset = delta == 0 ? 0 : _monthOffset + delta;
            UpdateCalendar(force: true);
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
            BatteryFill.Width = Math.Max(1.5, 18 * Math.Clamp(power.BatteryLifePercent, 0, 1));
            BatteryFill.Background = charging ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58))
                : percent <= 20 ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A))
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

        private async void Progress_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ProgressTrack.ActualWidth <= 0) return;
            double ratio = Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0, 1);
            e.Handled = true;
            await _media.SeekAsync(TimeSpan.FromSeconds(_media.Duration.TotalSeconds * ratio));
            UpdateProgress();
        }

        private void Progress_MouseEnter(object sender, MouseEventArgs e) => SetProgressThickness(7);
        private void Progress_MouseLeave(object sender, MouseEventArgs e) => SetProgressThickness(4);

        private void SetProgressThickness(double h)
        {
            var anim = new DoubleAnimation(h, TimeSpan.FromMilliseconds(140));
            ProgressTrack.BeginAnimation(HeightProperty, anim);
            ProgressFill.BeginAnimation(HeightProperty, anim);
            ProgressTrack.CornerRadius = ProgressFill.CornerRadius = new CornerRadius(Math.Min(2.5, h / 2));
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
            PlayPauseGlyph.Data = _media.IsPlaying ? GlyphPause : GlyphPlay;
            PipButton.Visibility = _settings.EnablePip && _media.FromBrowser ? Visibility.Visible : Visibility.Collapsed;

            WatchTrack();
            UpdateCollapsedState();
            if (_expanded) UpdateProgress();
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();

        /// <summary>
        /// Picture in picture: acha o vídeo do YouTube aberto no navegador, pausa lá e continua
        /// do mesmo ponto numa janela flutuante.
        /// </summary>
        private async void Pip_Click(object sender, RoutedEventArgs e)
        {
            PipButton.IsEnabled = false;
            string title = _media.Title;
            double position = _media.Position.TotalSeconds;
            try
            {
                string? id = await System.Threading.Tasks.Task.Run(() => YouTubeLocator.FromBrowsers(title));
                if (id != null)
                {
                    await _media.PauseAsync();
                    PipWindow.Open(id, position, title);
                }
                else
                {
                    // Não achou no navegador: tenta um link copiado; senão a janela pede o link.
                    PipWindow.Open(YouTubeLocator.FromClipboard(), 0);
                }
                _pinned = false;
                Collapse();
            }
            finally
            {
                PipButton.IsEnabled = true;
            }
        }
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
            if (Keyboard.FocusedElement is not TextBox) Native.SetNoActivate(_hwnd, true);
        }

        private void TextInput_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // O clique já ativou a janela (WS_EX_NOACTIVATE está desligado sobre as áreas de texto);
            // aqui só garantimos o foco do teclado na caixa.
            if (sender is TextBox box && !box.IsKeyboardFocusWithin) box.Focus();
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
            if (Keyboard.FocusedElement is TextBox) Keyboard.ClearFocus();
            if (IsForeground && _previousForeground != IntPtr.Zero && Native.IsWindow(_previousForeground))
                Native.SetForegroundWindow(_previousForeground);
            _previousForeground = IntPtr.Zero;
            if (!PageNotes.IsMouseOver && !TodoPanel.IsMouseOver) Native.SetNoActivate(_hwnd, true);
        }

        // ───────────── Tarefas ─────────────

        private void SaveTodos()
        {
            _settings.Todos = _todos.ToList();
            _settings.Save();
            UpdateTodoSummary();
        }

        private void UpdateTodoSummary()
        {
            int open = _todos.Count(t => !t.Done);
            TodoCount.Text = _todos.Count == 0 ? "" : open == 0 ? "TUDO FEITO" : $"{open} A FAZER";
            TodoEmpty.Visibility = _todos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddTodo()
        {
            string text = TodoInput.Text.Trim();
            if (text.Length == 0) return;
            _todos.Add(new TodoItem { Text = text });
            TodoInput.Clear();
        }

        private void TodoAdd_Click(object sender, RoutedEventArgs e) => AddTodo();

        private void TodoInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AddTodo();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ReleaseKeyboard();
                RestartCollapseTimer();
            }
        }

        private void TodoInput_TextChanged(object sender, TextChangedEventArgs e) =>
            TodoPlaceholder.Visibility = TodoInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        // O binding já atualizou "Done"; aqui só salva.
        private void TodoCheck_Click(object sender, RoutedEventArgs e) => SaveTodos();

        private void TodoRemove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TodoItem item) _todos.Remove(item);
        }

        // ───────────── Timer ─────────────

        private void TimerPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int minutes)) return;
            TimerMode.Text = "TIMER";
            StopAlert();
            _timerPreset = TimeSpan.FromMinutes(minutes);
            _timerPaused = _timerPreset;
            _timerEndsAt = DateTime.Now + _timerPreset;
            UpdateTimerUi();
        }

        /// <summary>Blocos Foco / Pausa / Pausa longa: Tag = "minutos|NOME".</summary>
        private void TimerMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag }) return;
            var parts = tag.Split('|');
            if (!int.TryParse(parts[0], out int minutes)) return;
            TimerMode.Text = parts.Length > 1 ? parts[1] : "TIMER";
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
            TimerStartGlyph.Data = TimerRunning ? GlyphPause : GlyphPlay;
            TimerStartText.Text = TimerRunning ? "Pausar" : remaining < _timerPreset && remaining > TimeSpan.Zero ? "Continuar" : "Iniciar";
            double total = Math.Max(_timerPreset.TotalSeconds, TimerRemaining.TotalSeconds);
            DrawTimerRing(total <= 0 ? 0 : TimerRemaining.TotalSeconds / total);
            TimerStatus.Text = _timerAlerting ? "Tempo esgotado!"
                : TimerRunning ? "Em andamento"
                : remaining < _timerPreset && remaining > TimeSpan.Zero ? "Pausado"
                : "Pronto";
            TimerStatus.Foreground = (Brush)Resources[_timerAlerting ? "Accent" : "Secondary"];
            UpdateCollapsedState();
        }

        // Contorno do timer: quadrado de cantos suaves, começando no meio do topo, sentido horário.
        private const double TimerSize = 156, TimerStroke = 6, TimerCorner = 38;
        private static readonly double TimerPerimeter =
            4 * (TimerSize - TimerStroke - 2 * TimerCorner) + 2 * Math.PI * TimerCorner;

        private static Geometry TimerOutline()
        {
            double a = TimerStroke / 2, b = TimerSize - TimerStroke / 2, r = TimerCorner, mid = TimerSize / 2;
            var size = new Size(r, r);
            var f = new PathFigure { StartPoint = new Point(mid, a), IsClosed = false };
            f.Segments.Add(new LineSegment(new Point(b - r, a), true));
            f.Segments.Add(new ArcSegment(new Point(b, a + r), size, 0, false, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(new Point(b, b - r), true));
            f.Segments.Add(new ArcSegment(new Point(b - r, b), size, 0, false, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(new Point(a + r, b), true));
            f.Segments.Add(new ArcSegment(new Point(a, b - r), size, 0, false, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(new Point(a, a + r), true));
            f.Segments.Add(new ArcSegment(new Point(a + r, a), size, 0, false, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(new Point(mid, a), true));
            var g = new PathGeometry();
            g.Figures.Add(f);
            g.Freeze();
            return g;
        }

        private void DrawTimerTrack()
        {
            var outline = TimerOutline();
            TimerTrack.Data = outline;
            TimerRing.Data = outline;
        }

        /// <summary>Mostra só a fração restante do contorno (traço tracejado com um único segmento).</summary>
        private void DrawTimerRing(double fraction)
        {
            fraction = Math.Clamp(fraction, 0, 1);
            TimerRing.Visibility = fraction <= 0.0005 ? Visibility.Hidden : Visibility.Visible;
            double dash = TimerPerimeter * fraction / TimerStroke; // em múltiplos da espessura
            TimerRing.StrokeDashArray = fraction >= 0.9995 ? null : new DoubleCollection { dash, 10000 };
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
                _glassWindow?.Show();
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
