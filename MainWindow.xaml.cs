using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
        private const double DefaultOpenWidth = 520, DefaultOpenHeight = 184;
        // Ilha flutuante redimensionável: limites do tamanho aberto.
        private const double MinFloatWidth = 340, MaxFloatWidth = 1040, MinFloatHeight = 130, MaxFloatHeight = 460;
        private double _expW = DefaultOpenWidth, _expH = DefaultOpenHeight;
        private const double CollapsedExtraWidth = 64;
        private const double PeekExtraWidth = 215;
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
        /// <summary>Dia escolhido no calendário; as tarefas mostradas e as novas são desse dia.</summary>
        private DateTime _selectedDate = DateTime.Today;
        private bool _agendaCompact;
        private double _cornerRadius = 22;
        private readonly SystemStats _stats = new();
        private readonly DownloadWatcher _downloads = new();
        private double _artScaleTarget = 1;
        private readonly ObservableCollection<TodoItem> _todos = new();
        private string _currentTab = "home";
        private readonly ObservableCollection<ShelfItem> _shelf = new();
        private readonly ObservableCollection<ClipItem> _clips = new();
        private readonly DispatcherTimer _expandTimer = new();
        private readonly DispatcherTimer _collapseTimer = new();
        private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(1.5) };
        private readonly DispatcherTimer _notesSave = new() { Interval = TimeSpan.FromMilliseconds(600) };
        /// <summary>Barra da música: atualiza 10 vezes por segundo com a ilha aberta (o segundo vira na hora certa).</summary>
        private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
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
            L.ApplyTo(this);
#if DEBUG
            // Testes visuais: muda só na memória (as configurações reais não são tocadas).
            if (Environment.GetEnvironmentVariable("ILHA_TEST_FLOAT") == "1") _settings.IslandStyle = "Floating";
            if (Environment.GetEnvironmentVariable("ILHA_TEST_STYLE") is { Length: > 0 } testStyle) _settings.IslandStyle = testStyle;
            if (Environment.GetEnvironmentVariable("ILHA_TEST_THEME") == "Light") SettingsWindow.SetTheme(_settings, light: true);
            if (Environment.GetEnvironmentVariable("ILHA_TEST_RESTART") == "1")
            {
                var restart = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                restart.Tick += (_, _) =>
                {
                    restart.Stop();
                    Environment.SetEnvironmentVariable("ILHA_TEST_RESTART", null); // a nova instância não reinicia de novo
                    App.Restart(ExitApp);
                };
                restart.Start();
            }
            if (Environment.GetEnvironmentVariable("ILHA_TEST_LAYOUT") is { Length: > 0 } testLayout) _settings.OpenLayout = testLayout;
            if (Environment.GetEnvironmentVariable("ILHA_TEST_DEMO") == "1")
            {
                // Dados de exemplo (só na memória do teste).
                _settings.ShowMedia = _settings.ShowCalendar = _settings.ShowTasks = _settings.ShowNotes = _settings.ShowTimer = true;
                foreach (var c in new[] { "Rua das Flores, 120", "https://yamamotodesigner.github.io/ilha", "Reunião às 15h na sala 2" })
                    _clips.Add(new ClipItem(c));
                _settings.DailyTasks = new List<TodoItem>
                {
                    new() { Text = "Responder e-mails", Done = true, Date = DateTime.Today.ToString("yyyy-MM-dd") },
                    new() { Text = "Revisar a apresentação", Date = DateTime.Today.ToString("yyyy-MM-dd") },
                    new() { Text = "Caminhar 30 minutos", Date = DateTime.Today.ToString("yyyy-MM-dd") },
                };
                _settings.Todos.Add(new TodoItem { Text = "Reunião com o time · 15h", Date = DateTime.Today.ToString("yyyy-MM-dd") });
                _settings.Todos.Add(new TodoItem { Text = "Dentista", Date = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd") });
                _settings.NotePages = new List<NotePage>
                {
                    new() { Title = "Ideias", Color = "yellow", Content = "<b>Fim de semana</b>\nFeira de sábado cedo\nTerminar o <i>livro</i>", Preview = "Fim de semana · Feira de sábado cedo" },
                    new() { Title = "Mercado", Color = "green", Content = "Café\nPão\nFrutas", Preview = "Café · Pão · Frutas" },
                    new() { Title = "Projeto Ilha", Color = "purple", Content = "Testar a ilha completa", Preview = "Testar a ilha completa" },
                };
            }
            if (Environment.GetEnvironmentVariable("ILHA_TEST_TABSTOP") == "1") _settings.TabPosition = "Top";
            if (Environment.GetEnvironmentVariable("ILHA_TEST_TABSTOP") == "0") _settings.TabPosition = "Side";
            if (Environment.GetEnvironmentVariable("ILHA_TEST_MATERIAL") is { Length: > 0 } testMaterial)
            {
                _settings.Material = testMaterial;
                if (testMaterial == "Glass") _settings.GlassTint = 42;
            }
            if (double.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_FW"), out double tfw)) _settings.OpenWidth = tfw;
            if (double.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_FH"), out double tfh)) _settings.OpenHeight = tfh;
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
            {
                ClipboardEmpty.Visibility = _clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                QueueDynamicResize();
            };
            ClipboardEmpty.Visibility = _clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            InitNotes();
            InitNudges();
            InitConvert();
            InitAlarm();
            // Já nasce do tamanho recolhido (sem crescer/encolher no primeiro instante).
            Island.Width = _settings.CollapsedWidth;
            Island.Height = _settings.CollapsedHeight;
            _progressTimer.Tick += (_, _) => UpdateProgress();

            foreach (var todo in _settings.Todos) _todos.Add(todo);
            TodoItems.ItemsSource = _todos;
            _todos.CollectionChanged += (_, _) => SaveTodos();
            RefreshTodoFilter();
            InitTasks();
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
            _notesSave.Tick += (_, _) => SaveOpenNote();
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
            DayStrip.SizeChanged += (_, _) => CenterDayStrip(animate: false);
            _downloads.Completed += OnDownloadCompleted;
            TabStrip.SizeChanged += (_, _) => MoveTabIndicator(animate: false);
            PrompterViewport.SizeChanged += (_, _) => SizePrompterLines();
            PrompterViewport.MouseWheel += PrompterViewport_MouseWheel;
            LoadPrompter();
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
            InitSlim();
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
            // Primeira abertura depois de atualizar: mostra as novidades desta versão.
            var whatsNew = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            whatsNew.Tick += (_, _) => { whatsNew.Stop(); ShowWhatsNewIfUpdated(); };
            whatsNew.Start();
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
                    if (testTab == "peek")
                    {
                        ShowPeek((Geometry)FindResource("IcoSpeaker"), true, (Geometry)FindResource("IcoWaves"),
                                 null, "Volume", 0.62, "62", seconds: 30);
                        return;
                    }
                    if (Environment.GetEnvironmentVariable("ILHA_TEST_NUDGE") is { Length: > 0 } nudge)
                    {
                        if (nudge == "hello") { _settings.UserName = Environment.GetEnvironmentVariable("ILHA_TEST_NAME") ?? "Lucas"; _settings.LastGreeting = ""; TryGreet(); }
                        else ShowWellness(nudge);
                        _nudgeTimer.Interval = TimeSpan.FromSeconds(60);
                        return;
                    }
                    if (testTab == "idle") return; // só a ilha recolhida, esperando avisos
                    _pinned = true;
                    Expand();
                    // ILHA_TEST_CYCLE="home,agenda": alterna as abas a cada 1,5 s (para ver piscadas na troca).
                    if (Environment.GetEnvironmentVariable("ILHA_TEST_CYCLE") is { Length: > 0 } cycle)
                    {
                        var tabs = cycle.Split(',');
                        int at = 0;
                        var swap = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                        swap.Tick += (_, _) =>
                        {
                            at = (at + 1) % tabs.Length;
                            foreach (var b in TabBar.Children.OfType<RadioButton>())
                                if ((string)b.Tag == tabs[at]) b.IsChecked = true;
                        };
                        swap.Start();
                    }
                    foreach (var tabButton in TabBar.Children.OfType<RadioButton>())
                        if ((string)tabButton.Tag == testTab) tabButton.IsChecked = true;
                    // Conversor: ILHA_TEST_CONVERT="arquivo" carrega; ILHA_TEST_CONVERT_RUN="png" já converte;
                    // ILHA_TEST_DROP=1 mostra a pergunta de arrastar e ILHA_TEST_CONNECT=1 toca o encaixe.
                    if (testTab == "convert" && Environment.GetEnvironmentVariable("ILHA_TEST_CONVERT") is { Length: > 0 } convertFile)
                    {
                        LoadConvert(convertFile.Split('|'));
                        if (Environment.GetEnvironmentVariable("ILHA_TEST_CONVERT_RUN") is { Length: > 0 } run &&
                            ConvertOptions.Children.OfType<Button>().FirstOrDefault(b => (string)b.Tag == run) is { } chip)
                            ConvertOption_Click(chip, new RoutedEventArgs());
                    }
                    if (Environment.GetEnvironmentVariable("ILHA_TEST_DROP") is { Length: > 0 } dropZone)
                    {
                        ShowDropOverlay(true);
                        SetDropZone(dropZone);
                    }
                    if (Environment.GetEnvironmentVariable("ILHA_TEST_CONNECT") == "1")
                    {
                        var connect = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                        connect.Tick += (_, _) => PlayConnect(shake: true);
                        connect.Start();
                    }
                    if (testTab == "notes" && Environment.GetEnvironmentVariable("ILHA_TEST_NOTE") == "open" && _settings.NotePages.Count > 0)
                        OpenNote(_settings.NotePages[0]);
                    if (testTab == "prompter" && Environment.GetEnvironmentVariable("ILHA_TEST_PROMPTER") == "start")
                        PrompterStart_Click(this, new RoutedEventArgs());
                    // Agenda: escolhe um dia daqui a N dias e cria um lembrete nele.
                    if (testTab == "agenda" && int.TryParse(Environment.GetEnvironmentVariable("ILHA_TEST_DAY"), out int plus))
                    {
                        SelectDay(DateTime.Today.AddDays(plus));
                        TodoInput.Text = "Consulta no dentista às 15h";
                        AddTodo();
                    }
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
            UpdateAmbientClip();
        }

        // ───────────── Ilha flutuante: arrastar e fixar ─────────────

        private void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (TryStopAlertByClick()) { e.Handled = true; return; } // ilha verde: clicar para o alarme
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
            PinButton.Foreground = _settings.FloatLocked ? (Brush)Resources["Accent"] : Ink(0xA6);
            PinButton.ToolTip = _settings.FloatLocked ? L.T("Posição fixada — clique para soltar e poder arrastar")
                                                      : L.T("Fixar a ilha neste lugar");
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
            // O tamanho escolhido vira uma proporção sobre o tamanho de cada aba.
            var (bw, bh) = BaseTabSize(_currentTab);
            _settings.OpenWidth = Math.Round(DefaultOpenWidth * _expW / bw);
            _settings.OpenHeight = Math.Round(DefaultOpenHeight * _expH / bh);
            _settings.Save();
            if (!Island.IsMouseOver) RestartCollapseTimer();
        }

        /// <summary>Abas no trilho lateral ou numa barra no topo do cabeçalho.</summary>
        private void ApplyTabPosition()
        {
            bool top = true; // abas sempre no topo: a ilha fica mais fina
            var currentHost = TabStrip.Parent as FrameworkElement;
            if (top && currentHost != TopTabsHost)
            {
                RailHost.Children.Remove(TabStrip);
                TopTabsHost.Child = TabStrip;
            }
            else if (!top && currentHost != RailHost)
            {
                TopTabsHost.Child = null;
                RailHost.Children.Add(TabStrip);
            }
            AddTabButton.Width = top ? 26 : 36;
            AddTabButton.Height = top ? 24 : 26;
            TabBar.Orientation = top ? Orientation.Horizontal : Orientation.Vertical;
            foreach (var tab in TabBar.Children.OfType<RadioButton>())
            {
                tab.Width = top ? 34 : 36;
                tab.Height = top ? 24 : 30; // na lateral, UpdateResponsiveLayout ajusta à altura disponível
                tab.Margin = top ? new Thickness(2, 0, 2, 0) : new Thickness(0, 0, 0, 6);
            }
            HeaderRow.Height = new GridLength(top ? HeaderTop : HeaderSide);
            RailCol.Width = new GridLength(top ? 0 : 36);
            RailGapCol.Width = new GridLength(top ? 0 : RailGap);
            RailLine.Visibility = Visibility.Collapsed; // sem linha divisória: o espaço já separa
            TopTabsHost.Visibility = top && TabBar.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
            TabStrip.Visibility = TabBar.Visibility;
            Dispatcher.BeginInvoke(() => MoveTabIndicator(animate: false), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// O destaque da aba escolhida desliza até ela (com um leve "esticar" no caminho),
        /// em vez de simplesmente trocar de lugar.
        /// </summary>
        private void MoveTabIndicator(bool animate)
        {
            var target = TabBar.Children.OfType<RadioButton>().FirstOrDefault(t => t.IsChecked == true && t.Visibility == Visibility.Visible);
            if (target == null || target.ActualWidth <= 0 || !TabStrip.IsAncestorOf(target))
            {
                TabIndicator.Visibility = Visibility.Hidden;
                return;
            }
            TabIndicator.Visibility = Visibility.Visible;
            var p = target.TranslatePoint(new Point(0, 0), TabStrip);
            var dur = TimeSpan.FromMilliseconds(animate && !ReduceMotion ? 460 : 0);
            IEasingFunction ease = new SpringEase { Damping = 0.8, Frequency = 11 };
            TabIndicator.BeginAnimation(WidthProperty, new DoubleAnimation(target.ActualWidth, dur) { EasingFunction = ease });
            TabIndicator.BeginAnimation(HeightProperty, new DoubleAnimation(target.ActualHeight, dur) { EasingFunction = ease });
            TabIndicatorShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(p.X, dur) { EasingFunction = ease });
            TabIndicatorShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(p.Y, dur) { EasingFunction = ease });
        }

        // Abaixo deste tamanho de conteúdo, tudo encolhe por igual em vez de cortar.
        private const double ContentMinWidth = 420, ContentMinHeight = 150;
        // Respiro: margens largas em volta do conteúdo (ver Margin de ContentRoot no XAML).
        private const double ContentPadX = 40, ContentPadY = 30;
        private const double RailWidth = 36, RailGap = 18, HeaderSide = 28, HeaderTop = 32, PageGap = 12;

        private bool TabsOnTop => true; // abas sempre no topo

        /// <summary>
        /// Cada aba tem o seu tamanho: a ilha cresce ou encolhe para o que a tela pede
        /// (a música fica pequena, o calendário ganha espaço), sem nada espremido.
        /// </summary>
        private static (double W, double H) BaseTabSize(string tab) => tab switch
        {
            "home" => (460, 176),
            "shelf" => (460, 188),
            "convert" => (480, 198),
            "agenda" => (540, 268),
            "tasks" => (440, 236),
            "notes" => (500, 236),
            "timer" => (580, 250),
            "clipboard" => (480, 250),
            "prompter" => (580, 236),
            "mirror" => (460, 280),
            _ => (DefaultOpenWidth, DefaultOpenHeight),
        };

        /// <summary>Aplica o tamanho da aba atual, na proporção que a pessoa escolheu nas configurações.</summary>
        private void ApplyTabSize(bool animate)
        {
            var (bw, bh) = BaseTabSize(_currentTab);
            double kw = Math.Clamp(_settings.OpenWidth / DefaultOpenWidth, 0.7, 1.6);
            double kh = Math.Clamp(_settings.OpenHeight / DefaultOpenHeight, 0.7, 1.6);
            _overviewK = 1;
            _dynamicHeight = false;
            if (InOverview)
            {
                // Ilha completa: tudo lado a lado. Se não couber na tela, encolhe tudo por igual.
                double natural = OverviewContentWidth() + ContentPadX;
                double avail = AvailableIslandWidth();
                _overviewK = Math.Min(1, avail / natural);
                double h = OverviewHeight * Math.Clamp(kh, 0.9, 1.3);
                _expW = ContentPadX + (natural - ContentPadX) * _overviewK;
                _expH = ContentPadY + (h - ContentPadY) * _overviewK;
            }
            else
            {
                _expW = Math.Clamp(bw * kw, MinFloatWidth, MaxFloatWidth);
                _expH = Math.Clamp(bh * kh, MinFloatHeight, MaxFloatHeight);
                // Ilha dinâmica: copiados, tarefas, lembretes e páginas ditam a altura.
                // Nunca mais estreita que a barra de abas (centralizada) com o relógio e os botões dos lados.
                int tabs = TabBar.Children.OfType<RadioButton>().Count(t => t.Visibility == Visibility.Visible);
                double tabsW = TabBar.Visibility == Visibility.Visible ? tabs * 38 + 34 : 0;
                _expW = Math.Min(MaxFloatWidth, Math.Max(_expW, ContentPadX + tabsW + 2 * 92));
                var dynamic = DynamicPageHeight(_currentTab);
                _dynamicHeight = dynamic != null;
                if (dynamic is double ph)
                    _expH = Math.Clamp(ContentPadY + HeaderTop + PageGap + ph, 120, MaxFloatHeight);
            }
            ExpandedView.Width = _expW;
            ExpandedView.Height = _expH;
            // O brilho da capa cobre a ilha inteira, mesmo a completa (bem larga).
            AmbientArt.Width = Math.Max(1100, _expW + 300);
            UpdateResponsiveLayout();
            if (_expanded) AnimateSize(_expW, _expH, animate && _settings.SpringAnimation);
        }

        /// <summary>
        /// Encaixa o conteúdo no tamanho atual da ilha aberta: escala quando fica pequeno demais e
        /// esconde o que não cabe (sistema, prateleira, tarefas, modos do timer, data...).
        /// Poucos blocos por tela, com espaço entre eles, em vez de tudo espremido.
        /// </summary>
        private void UpdateResponsiveLayout()
        {
            var s = _settings;
            double cw = _expW - ContentPadX, ch = _expH - ContentPadY;
            double k = InOverview ? _overviewK
                : _dynamicHeight ? Math.Min(1, cw / ContentMinWidth) // a altura já é a do conteúdo
                : Math.Min(1, Math.Min(cw / ContentMinWidth, ch / ContentMinHeight));
            ContentScale.ScaleX = ContentScale.ScaleY = k;
            double vw = cw / k, vh = ch / k; // tamanho "virtual" em que o layout é montado
            ContentRoot.Width = vw;
            ContentRoot.Height = vh;

            bool top = TabsOnTop;
            double pageW = vw - (top ? 0 : RailWidth + RailGap);
            double pageH = vh - (top ? HeaderTop : HeaderSide) - PageGap;
            var visible = Visibility.Visible;
            var hidden = Visibility.Collapsed;

            // Trilho lateral: as abas dividem a altura disponível, com folga entre elas, sem cortar a última.
            if (!top)
            {
                var tabs = TabBar.Children.OfType<RadioButton>().Where(t => t.Visibility == visible).ToList();
                double addH = Math.Clamp(vh * 0.11, 18, 26); // o botão "+" também precisa caber
                double slot = tabs.Count == 0 ? 36 : (vh - addH) / tabs.Count;
                double gap = Math.Clamp(slot - 28, 2, 7);
                double tabH = Math.Clamp(slot - gap, 18, 30);
                foreach (var t in tabs)
                {
                    t.Height = tabH;
                    t.Margin = new Thickness(0, 0, 0, gap);
                }
                AddTabButton.Height = addH;
            }

            // Cabeçalho
            DateText.Visibility = (top ? vw >= 660 : pageW >= 330) ? visible : hidden;
            BatteryText.Visibility = !top || vw >= 580 ? visible : hidden;

            // Na ilha completa cada bloco tem a sua largura; nas abas, cada página usa a largura toda.
            double W(string tab) => InOverview && _overview.Contains(tab) ? OverviewWidth(tab) : pageW;

            // Início: a música; o monitor do sistema entra quando a ilha é mais larga.
            const double gap1 = 24, statsW = 124;
            double homeW = W("home");
            bool media = s.ShowMedia;
            bool stats = s.ShowStats && pageH >= 120 && (!media || (!InOverview && homeW >= 230 + gap1 + statsW + 60));
            MediaArea.Visibility = media ? visible : hidden;
            ShelfZone.Visibility = visible; // na aba própria, sempre inteira
            StatsCard.Visibility = stats ? visible : hidden;
            var star = new GridLength(1, GridUnitType.Star);
            HomeMediaCol.Width = media ? star : new GridLength(0);
            HomeStatsCol.Width = !stats ? new GridLength(0) : media ? new GridLength(statsW) : star;
            HomeShelfCol.Width = new GridLength(0);
            HomeGap1.Width = new GridLength(media && stats ? gap1 : 0);
            HomeGap2.Width = new GridLength(0);
            // Capa: o maior quadrado que cabe na altura, deixando espaço para os controles ao lado
            double mediaW = homeW - (stats ? statsW + gap1 : 0);
            bool shuffleRow = s.ShowShuffleRepeat;
            double controlsW = shuffleRow ? 214 : 160;
            double artSize = Math.Round(Math.Clamp(Math.Min(pageH - 16, mediaW - controlsW - 26), 40, 132));
            ArtBox.Width = ArtBox.Height = artSize;
            ArtBox.CornerRadius = new CornerRadius(Math.Round(artSize * 0.17));
            SourceText.Visibility = pageH >= 118 ? visible : hidden;
            // Pouco espaço ao lado da capa: botões um pouco menores.
            bool tight = mediaW - artSize - 22 < 236;
            PrevButton.Width = NextButton.Width = tight ? 32 : 40;
            PlayPauseButton.Margin = new Thickness(tight ? 4 : 8, 0, tight ? 4 : 8, 0);
            ShuffleButton.Margin = new Thickness(0, 0, tight ? 2 : 6, 0);
            RepeatButton.Margin = new Thickness(tight ? 2 : 6, 0, 0, 0);
            UpdateShuffleRepeatUi();

            // Agenda: a faixa de dias e os lembretes; com pouca altura, sai o título do dia.
            TodoHeader.Visibility = pageH >= 150 ? visible : hidden;

            // Foco: o quadrado encolhe com a altura; modos e atalhos só se couber.
            // Estreito (ilha completa): só o quadrado e dois botões redondos ao lado.
            double timerW = W("timer");
            bool timerNarrow = timerW < 330;
            TimerModes.Visibility = !timerNarrow && pageH >= 96 ? visible : hidden;
            TimerChips.Visibility = !timerNarrow && pageH >= 126 ? visible : hidden;
            TimerButtonsRow.Orientation = timerNarrow ? Orientation.Vertical : Orientation.Horizontal;
            TimerButtonsRow.Margin = new Thickness(0, 0, 0, timerNarrow ? 0 : 12);
            TimerStartText.Visibility = TimerResetText.Visibility = timerNarrow ? hidden : visible;
            TimerStartGlyph.Margin = new Thickness(0, 0, timerNarrow ? 0 : 7, 0);
            TimerResetGlyph.Margin = new Thickness(0, 0, timerNarrow ? 0 : 6, 0);
            TimerStartButton.Padding = timerNarrow ? new Thickness(11) : new Thickness(16, 8, 16, 8);
            TimerResetButton.Padding = timerNarrow ? new Thickness(11) : new Thickness(14, 8, 14, 8);
            TimerStartButton.Margin = timerNarrow ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 8, 0);
            TimerRingBox.Margin = new Thickness(0, 0, timerNarrow ? 14 : 28, 0);
            TimerRingBox.MaxWidth = timerNarrow ? Math.Max(60, timerW - 14 - 42) : double.PositiveInfinity;
            // Espelho
            double mirror = Math.Max(80, Math.Min(pageH, pageW));
            MirrorFrame.Width = MirrorFrame.Height = mirror;
            MirrorFrame.CornerRadius = new CornerRadius(mirror / 2);
            MirrorImage.CornerRadius = new CornerRadius(mirror / 2);

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
            double left = (ActualWidth - w) / 2 + IslandShake.X * scale; // acompanha a chacoalhada do encaixe
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

            SlimFill.Background = new SolidColorBrush(Color.FromArgb(0xEB, baseColor.R, baseColor.G, baseColor.B));

            // Luz ambiente: um véu da cor da ilha por cima do brilho mantém o texto legível
            // (mais forte embaixo e à direita, onde ficam os textos menores).
            byte veil = ThemePalette.IsDark ? (byte)0x52 : (byte)0x70;
            AmbientShade.Background = new LinearGradientBrush(
                Color.FromArgb((byte)(veil * 0.55), baseColor.R, baseColor.G, baseColor.B),
                Color.FromArgb((byte)Math.Min(255, veil * 1.7), baseColor.R, baseColor.G, baseColor.B),
                new Point(0, 0), new Point(1, 1));
            UpdateAmbient(force: true);

            UpdateGlassRegion();
        }

        private void WatchFullscreen()
        {
            if (_hwnd == IntPtr.Zero) return;
            RefreshForeground(); // reserva, caso o aviso de troca de janela não chegue
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
            SaveOpenNote();
            _settings.Save();
            if (_hotkeyRegistered) Native.UnregisterHotKey(_hwnd, HotkeyId);
            if (_clipboardListening) Native.RemoveClipboardFormatListener(_hwnd);
            Native.StopWatching(_foregroundHook);
            Native.StopWatching(_windowHook);
            _camera.Stop();
            _downloads.Dispose();
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

            UpdateButton.Content = L.F("Atualizar para {0}", info.Version);
            UpdateButton.ToolTip = L.T("Baixa e instala a nova versão; o Ilha reabre sozinho.");
            UpdateButton.IsEnabled = true;
            UpdateButton.Visibility = Visibility.Visible;
            _tray?.SetUpdate(L.F("Instalar atualização {0}", info.Version), () => _ = InstallUpdateAsync(), notify && isNew);
            if (notify && isNew && _settings.LiveUpdate)
                ShowPeek((Geometry)FindResource("IcoRefresh"), false, null, null, L.F("Versão {0} disponível", info.Version), null,
                         L.T("Atualizar"), (Brush)Resources["Accent"], seconds: 5);
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
                    return L.F("Você já está na versão mais recente ({0}).", UpdateService.CurrentVersion);
                _ = InstallUpdateAsync();
                return L.F("Baixando a versão {0}… o Ilha vai reabrir sozinho.", _availableUpdate!.Version);
            }
            catch (Exception ex)
            {
                return L.T("Não foi possível verificar: ") + ex.Message;
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
                var progress = new Progress<double>(p => UpdateButton.Content = L.T("Baixando…") + $" {p:P0}");
                string installer = await _updates.DownloadAsync(info, progress);
                UpdateButton.Content = L.T("Instalando…");
                UpdateService.LaunchInstaller(installer);
                ExitApp(); // o instalador substitui o exe e reabre o Ilha
            }
            catch (Exception ex)
            {
                _installingUpdate = false;
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = L.T("Tentar atualizar de novo");
                UpdateButton.ToolTip = L.T("Falhou: ") + ex.Message;
            }
        }

        // ───────────── Configurações ─────────────

        private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

        /// <summary>Botão "+" das abas: abre o catálogo de recursos nas configurações.</summary>
        private void AddFeatures_Click(object sender, RoutedEventArgs e)
        {
            OpenSettings();
            _settingsWindow?.ShowFeatures();
        }

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
            _settingsWindow.RestartRequested += () => App.Restart(ExitApp);
            _settingsWindow.OnboardingRequested += ReplayOnboarding;
            _settingsWindow.CheckForUpdates = CheckUpdatesFromSettingsAsync;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
            Collapse();
        }

        /// <summary>Mostra a apresentação de novo; as escolhas valem na hora (idioma novo reinicia o app).</summary>
        private void ReplayOnboarding()
        {
            _settingsWindow?.Close();
            string langBefore = L.Lang;
            var welcome = new SetupWindow(_settings, replay: true);
            welcome.Finished += () =>
            {
                if (L.Lang != langBefore) { App.Restart(ExitApp); return; }
                HoldFullSize(3.5);
                ApplySettings();
                welcome.FadeOutAndClose(delayMs: 150);
            };
            welcome.Show();
        }

        /// <summary>Uma vez por versão: a tela "Novidades" (se a versão tiver notas).</summary>
        private void ShowWhatsNewIfUpdated()
        {
            string version = WhatsNewWindow.CurrentVersion;
            bool force = false;
#if DEBUG
            force = Environment.GetEnvironmentVariable("ILHA_TEST_WHATSNEW") == "1";
            if (!force && Environment.GetEnvironmentVariable("ILHA_TEST_TAB") is { Length: > 0 }) return;
#endif
            if (!force && _settings.LastSeenVersion == version) return;
            _settings.LastSeenVersion = version;
            _settings.Save();
            if (ReleaseNotes.For(version) is not { } slides) return;
            // Na frente das outras janelas (o app abre sozinho depois da atualização, sem foco).
            var window = new WhatsNewWindow(_settings, version, slides) { Topmost = true };
            window.Show();
            window.Activate();
        }

        private void ApplySettings()
        {
            var s = _settings;
            _media.OnlyMainApps = s.MediaOnlyMainApps;

            // Tema, cores e material
            bool dark = !string.Equals(s.Theme, "Light", StringComparison.OrdinalIgnoreCase);
            if (dark != ThemePalette.IsDark) ThemePalette.Apply(dark);
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
            // Tamanho da ilha aberta (o mesmo em todos os estilos; na "Livre" o canto também muda).
            // A janela (transparente) já comporta o máximo, para redimensionar sem mover a ilha.
            RefreshOverviewList();
            Width = (Math.Max(MaxFloatWidth, FullLayout ? MaxOverviewWidth : 0) + 60) * scale;
            Height = (MaxFloatHeight + 56) * scale;
            ApplyTabSize(animate: false);

            // Tempos
            _expandTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, s.HoverDelayMs));
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, s.CollapseDelayMs));

            UpdateLikeUi();
            _downloads.Enabled = s.LiveDownloads || (s.DownloadsToShelf && s.ShowShelf);

            // Abas
            // Ilha completa: a primeira aba ("Tudo") mostra música, agenda, tarefas, notas e timer juntos.
            bool full = FullLayout;
            static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
            TabHome.Visibility = Vis(full ? _overview.Count > 0 : s.ShowMedia || s.ShowStats);
            HomeTabGlyph.Data = (Geometry)FindResource(full ? "FillLayout" : "FillHome");
            TabHome.ToolTip = L.T(full ? "Tudo" : "Início");
            TabShelf.Visibility = Vis(s.ShowShelf);
            TabConvert.Visibility = Vis(s.ShowConverter);
            TabAgenda.Visibility = Vis(s.ShowCalendar && !full);
            TabTasks.Visibility = Vis(s.ShowTasks && !full);
            TabNotes.Visibility = Vis(s.ShowNotes && !full);
            TabTimer.Visibility = Vis(s.ShowTimer && !full);
            TabClipboard.Visibility = s.ShowClipboard ? Visibility.Visible : Visibility.Collapsed;
            TabMirror.Visibility = s.ShowMirror ? Visibility.Visible : Visibility.Collapsed;
            TabPrompter.Visibility = s.ShowPrompter ? Visibility.Visible : Visibility.Collapsed;
            var visibleTabs = TabBar.Children.OfType<RadioButton>()
                .Where(t => t.Visibility == Visibility.Visible).ToList();
            TabBar.Visibility = visibleTabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            ApplyTabPosition();
            UpdateResponsiveLayout();
            if (visibleTabs.Count == 0)
                SelectTab("home");
            else if (!visibleTabs.Any(t => t.IsChecked == true))
                visibleTabs[0].IsChecked = true;
            // Trocou entre abas e ilha completa: refaz as colunas e o tamanho da aba atual.
            ApplyOverviewColumns();
            SelectTab(_currentTab);
            ApplyTabSize(animate: false);

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
            RefreshForeground();
            UpdateSlimWatch();
            UpdateCollapsedState(force: true);
        }

        /// <summary>Cor do texto/ícones do tema atual com a transparência pedida.</summary>
        private Brush Ink(byte alpha) => (Brush)FindResource($"Ink_{alpha:X2}");

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
            _draggingOut || _pinned || _timerAlerting || _scrubbing || Island.IsMouseOver || _dropHold || _dropActive ||
            _prompterLocked || _prompterRunning || _prompterCounting ||
            (IsForeground && Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase);

        /// <summary>
        /// Se a ilha é de fato a janela em primeiro plano. Window.IsActive do WPF não serve aqui:
        /// com WS_EX_NOACTIVATE ele pode ficar "true" sem a janela ter o foco real.
        /// </summary>
        private bool IsForeground => _hwnd != IntPtr.Zero && Native.ForegroundWindow() == _hwnd;

        private void Island_MouseEnter(object sender, MouseEventArgs e)
        {
            _collapseTimer.Stop();
            if (_alertShowing) { Fade(AlertOk, 1, 180); return; } // tempo esgotado: aparece o OK, a ilha não abre
            if (TryGreet()) return; // primeira vez do dia: "Bom dia, Nome!" e depois abre
            // No risco, uma pequena espera evita abrir sem querer ao passar pelo topo da tela.
            int delay = Math.Max(_settings.HoverDelayMs, SlimWanted && !_expanded ? 140 : 0);
            if (delay <= 0)
            {
                Expand();
                return;
            }
            _expandTimer.Interval = TimeSpan.FromMilliseconds(delay);
            _expandTimer.Start();
        }

        private void Island_MouseLeave(object sender, MouseEventArgs e)
        {
            _expandTimer.Stop();
            if (_alertShowing) Fade(AlertOk, 0, 220);
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
            if (_timerAlerting) { StopAlert(); return; } // o atalho também serve de OK
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
            if (_nudging) EndNudge(runAfter: false);
            _progressTimer.Start();

            AnimateSize(_expW, _expH, _settings.SpringAnimation);
            Fade(SlimFill, 0, 100);
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
            UpdateAmbientVisibility(animate: true);
            if (CurrentPage is { } current) StaggerIn(current);
            if (_currentTab == "mirror") StartMirror();
        }

        private void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            _pinned = false;
            _progressTimer.Stop();
            ExpandedView.IsHitTestVisible = false;
            Fade(ExpandedView, 0, 110);
            UpdateAmbientVisibility(animate: true);
            ShowDropOverlay(false);
            ReleaseKeyboard();
            StopMirror();
            if (_prompterRunning || _prompterCounting) PrompterStop(resetToStart: false);
            // A agenda volta para hoje: na próxima vez que abrir, não precisa rolar de volta.
            if (_selectedDate != DateTime.Today) SelectDay(DateTime.Today);
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
            if (_alertShowing && !_expanded) return; // o cartão verde do tempo esgotado ocupa a ilha
            if (_nudging && !_expanded) return; // a saudação/lembrete está ocupando a ilha

            // Com um app na frente, a ilha recolhida vira só um risco (ver MainWindow.Slim.cs).
            bool slim = SlimWanted && !_peeking;
            string key = $"{any}|{_peeking}|{s.CollapsedWidth}|{s.CollapsedHeight}|{slim}";
            if (_expanded || (!force && key == _collapsedKey)) return;
            _collapsedKey = key;
            Fade(SlimFill, slim ? 1 : 0, slim ? 220 : 120, delayMs: slim ? 140 : 0);
            if (slim)
            {
                AnimateSize(SlimWidth, SlimHeight, spring: false);
                Fade(CollapsedView, 0, 120);
                Fade(PeekView, 0, 120);
                return;
            }

            double width = s.CollapsedWidth + (_peeking ? PeekExtraWidth : any ? CollapsedExtraWidth : 0);
            AnimateSize(width, s.CollapsedHeight, spring: _settings.SpringAnimation && _peeking);
            Fade(CollapsedView, any && !_peeking ? 1 : 0, 200, delayMs: any && !_peeking ? 120 : 0);
            Fade(PeekView, _peeking ? 1 : 0, _peeking ? 220 : 140, delayMs: _peeking ? 90 : 0);
        }

        private void AnimateSize(double width, double height, bool spring)
        {
            spring &= !ReduceMotion;
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
            if (_expanded || _hiddenForFullscreen || _nudging || _alertShowing) return;
            PeekGlyph.Data = icon;
            PeekGlyph.Filled = filled;
            PeekGlyph2.Data = icon2;
            PeekGlyph.Foreground = PeekGlyph2.Foreground = accent ?? Ink(0xFF);
            PeekArtBrush.ImageSource = art;
            PeekArt.Visibility = art != null ? Visibility.Visible : Visibility.Collapsed;
            PeekGlyph.Visibility = PeekGlyph2.Visibility = art != null ? Visibility.Collapsed : Visibility.Visible;
            PeekText.Text = text;
            PeekValue.Text = value;
            PeekValue.Visibility = value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            PeekBar.Visibility = level.HasValue ? Visibility.Visible : Visibility.Collapsed;
            PeekValue.Margin = new Thickness(level.HasValue ? 8 : 0, 0, 0, 0);
            bool right = level.HasValue || value.Length > 0;
            PeekRight.Visibility = right ? Visibility.Visible : Visibility.Collapsed;
            // O grupo todo fica no centro da ilha; o texto só corta se não couber.
            double rightW = right ? 14 + (level.HasValue ? 70 + 8 : 0) + (value.Length > 0 ? 34 : 0) : 0;
            PeekText.MaxWidth = Math.Max(60, _settings.CollapsedWidth + PeekExtraWidth - 24 - 22 - 10 - rightW);
            if (level is double l)
            {
                PeekBarFill.Background = accent ?? Ink(0xFF);
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
            ShowPeek(speaker, true, extra, null, muted ? L.T("Mudo") : L.T("Volume"), muted ? 0 : level,
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
                     charging ? L.T("Carregando") : L.T("Na bateria"), power.BatteryLifePercent, $"{percent}%",
                     charging ? green : null, seconds: 2.6);
        }

        /// <summary>Download terminou: aviso na ilha e (se ligado) o arquivo vai para a prateleira.</summary>
        private void OnDownloadCompleted(string path, long size)
        {
            if (_settings.DownloadsToShelf && _settings.ShowShelf &&
                !_shelf.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                var item = ShelfItem.From(path);
                if (item.Exists()) _shelf.Add(item);
            }
            if (!_settings.LiveDownloads) return;
            string ext = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            ShowPeek((Geometry)FindResource("IcoTray"), false, null, null,
                     L.T("Download concluído") + " · " + DownloadWatcher.FormatSize(size), null,
                     ext.Length is > 0 and <= 5 ? ext : "", (Brush)Resources["Accent"], seconds: 3.4);
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

        private static readonly string[] TabOrder = { "home", "shelf", "convert", "agenda", "tasks", "notes", "timer", "clipboard", "mirror", "prompter" };

        private (string Tab, FrameworkElement Page)[] AllPages => new (string, FrameworkElement)[]
        {
            ("home", PageHome), ("shelf", PageShelf), ("convert", PageConvert), ("agenda", PageAgenda), ("tasks", PageTasks), ("notes", PageNotes), ("timer", PageTimer),
            ("clipboard", PageClipboard), ("mirror", PageMirror), ("prompter", PagePrompter),
        };

        /// <summary>Sem animações quando o Windows pede menos movimento.</summary>
        private static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;

        /// <summary>Imagem da página como está agora, no mesmo lugar dentro de <c>Pages</c>.</summary>
        private Image? SnapshotPage(FrameworkElement page)
        {
            try
            {
                double w = page.ActualWidth, h = page.ActualHeight;
                if (w < 2 || h < 2) return null;
                var dpi = VisualTreeHelper.GetDpi(page);
                var at = page.TranslatePoint(new Point(0, 0), LeavingLayer);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                    dc.DrawRectangle(new VisualBrush(page) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                        null, new Rect(0, 0, w, h));
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
                    96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bmp.Render(visual);
                bmp.Freeze();
                var image = new Image { Source = bmp, Width = w, Height = h, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(image, at.X);
                Canvas.SetTop(image, at.Y);
                return image;
            }
            catch
            {
                return null;
            }
        }

        private void SelectTab(string tab)
        {
            if (PageHome == null) return; // durante InitializeComponent
            string previous = _currentTab;
            bool changed = tab != previous;
            _currentTab = tab;
            // Entrar ou sair da ilha completa muda as colunas: aí a troca é direta, sem cruzar as páginas.
            bool layoutChanged = changed && FullLayout && (tab == "home" || previous == "home");
            if (layoutChanged) ApplyOverviewColumns();

            // Direção: para frente desliza de um lado, para trás do outro (vertical com abas na lateral).
            int dir = Math.Sign(Array.IndexOf(TabOrder, tab) - Array.IndexOf(TabOrder, previous));
            bool vertical = !TabsOnTop;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            foreach (var (name, page) in AllPages)
            {
                bool show = Shown(name, tab);
                if (show)
                {
                    page.Visibility = Visibility.Visible;
                    page.IsHitTestVisible = true;
                    Panel.SetZIndex(page, 1);
                    if (!changed) continue;
                    if (ReduceMotion)
                    {
                        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
                        continue;
                    }
                    // Entra: aparece, desliza 18 px e cresce de 0,97 a 1, tudo junto.
                    var move = new TranslateTransform();
                    var grow = new ScaleTransform(0.97, 0.97);
                    page.RenderTransformOrigin = new Point(0.5, 0.5);
                    page.RenderTransform = new TransformGroup { Children = { grow, move } };
                    var dur = TimeSpan.FromMilliseconds(380);
                    page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
                    (vertical ? (Action)(() => move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18 * dir, 0, dur) { EasingFunction = ease }))
                              : () => move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(22 * dir, 0, dur) { EasingFunction = ease }))();
                    grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
                    grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
                }
                else if (changed && !layoutChanged && Shown(name, previous) && page.Visibility == Visibility.Visible && !ReduceMotion)
                {
                    // Sai: some rápido para o lado oposto enquanto a nova entra (troca cruzada).
                    // Quem sai é uma "foto" da página: a ilha já se reorganiza para o tamanho da próxima aba,
                    // e a página de verdade, espremida, perdia cartões por um quadro (parecia uma piscada).
                    var shot = SnapshotPage(page);
                    if (shot != null)
                    {
                        page.Visibility = Visibility.Collapsed;
                        page.BeginAnimation(OpacityProperty, null);
                        page.Opacity = 1;
                        page.IsHitTestVisible = true;
                        LeavingLayer.Children.Add(shot);
                        Pages.ClipToBounds = false; // quem corta é a própria ilha, que encolhe animando
                        var shotMove = new TranslateTransform();
                        shot.RenderTransform = shotMove;
                        var shotFade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)); // cruza com a nova (240 ms)
                        // A foto não pode passar da borda da ilha, que encolhe animando: recorta a cada quadro.
                        EventHandler clipToIsland = (_, _) =>
                        {
                            try
                            {
                                var origin = Island.TranslatePoint(new Point(0, 0), shot);
                                double bottom = origin.Y + Island.ActualHeight - 8;
                                shot.Clip = new RectangleGeometry(new Rect(-40, -40, shot.Width + 80, Math.Max(0, bottom + 40)));
                            }
                            catch (InvalidOperationException)
                            {
                                // Fora da árvore visual (já removida).
                            }
                        };
                        clipToIsland(null, EventArgs.Empty);
                        CompositionTarget.Rendering += clipToIsland;
                        shotFade.Completed += (_, _) =>
                        {
                            CompositionTarget.Rendering -= clipToIsland;
                            LeavingLayer.Children.Remove(shot);
                            if (LeavingLayer.Children.Count == 0) Pages.ClipToBounds = true;
                        };
                        shot.BeginAnimation(OpacityProperty, shotFade);
                        shotMove.BeginAnimation(vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty,
                            new DoubleAnimation(-12 * dir, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                        continue;
                    }
                    Panel.SetZIndex(page, 0);
                    page.IsHitTestVisible = false;
                    var move = new TranslateTransform();
                    page.RenderTransform = move;
                    var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
                    var leaving = page;
                    fade.Completed += (_, _) =>
                    {
                        if (Shown(name, _currentTab)) return; // voltou para ela no meio da troca
                        leaving.Visibility = Visibility.Collapsed;
                        leaving.BeginAnimation(OpacityProperty, null);
                        leaving.Opacity = 1;
                        leaving.IsHitTestVisible = true;
                    };
                    page.BeginAnimation(OpacityProperty, fade);
                    var shiftOut = new DoubleAnimation(-12 * dir, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
                    move.BeginAnimation(vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty, shiftOut);
                }
                else
                {
                    page.Visibility = Visibility.Collapsed;
                }
            }
            MoveTabIndicator(animate: changed);
            if (changed) ApplyTabSize(animate: _expanded);
            if (changed && _expanded && CurrentPage is { } entering) StaggerIn(entering, fade: false); // a página já entra com fade: apagar os blocos também deixava um quadro quase preto (piscada)
            if (tab != "prompter" && previous == "prompter" && _prompterRunning) PrompterStop(resetToStart: false);
            if (changed && !Shown("notes", tab) && !Shown("agenda", tab) && !Shown("tasks", tab) && tab != "prompter") ReleaseKeyboard();
            if (changed && Shown("agenda", tab) && !Shown("agenda", previous) && _selectedDate != DateTime.Today) SelectDay(DateTime.Today); // já em hoje: não refaz a lista (piscava)
            if (changed && Shown("notes", previous) && !Shown("notes", tab)) SaveOpenNote();
            if (tab == "mirror" && _expanded) StartMirror();
            else StopMirror();
        }

        // ───────────── Espelho (câmera) ─────────────

        private async void StartMirror()
        {
            if (_camera.IsRunning) return;
            MirrorPlaceholder.Visibility = Visibility.Visible;
            MirrorRetry.Visibility = Visibility.Collapsed;
            MirrorStatus.Text = L.T("Ligando a câmera…");
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
            MirrorStatus.Text = L.T("Ligando a câmera…");
        }

        private void MirrorRetry_Click(object sender, RoutedEventArgs e) => StartMirror();

        // ───────────── Relógio, bateria, calendário, progresso ─────────────

        private int _tickCount;

        private void OnTick()
        {
            UpdateTimer();
            WatchVolume();
            if (_tickCount % 4 == 0) CheckNudges();
            bool everySecond = ++_tickCount % 4 == 0;
            if (everySecond)
            {
                WatchCharging();
                _stats.Sample();
                _media.Resync();
                CheckChime();
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
                : Ink(0xFF);
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
            DurationText.Text = FormatTime(duration);
            if (_scrubbing) return; // enquanto arrasta, a barra segue o dedo, não a música
            var position = _media.Position;
            double ratio = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
            ProgressFill.Width = ProgressTrack.ActualWidth * ratio;
            PositionText.Text = FormatTime(position);
        }

        // Barra da música: segurar e arrastar escolhe o ponto exato; a música só pula ao soltar.
        private bool _scrubbing;
        private double _scrubRatio;

        private void Progress_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ProgressTrack.ActualWidth <= 0 || _media.Duration <= TimeSpan.Zero) return;
            e.Handled = true;
            _scrubbing = true;
            ProgressHit.CaptureMouse();
            ScrubTo(e.GetPosition(ProgressTrack).X);
        }

        private void Progress_MouseMove(object sender, MouseEventArgs e)
        {
            if (_scrubbing) ScrubTo(e.GetPosition(ProgressTrack).X);
        }

        private void Progress_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_scrubbing) return;
            ScrubTo(e.GetPosition(ProgressTrack).X);
            ProgressHit.ReleaseMouseCapture(); // LostMouseCapture faz o resto
        }

        private async void Progress_LostCapture(object sender, MouseEventArgs e)
        {
            if (!_scrubbing) return;
            double ratio = _scrubRatio;
            await _media.SeekAsync(TimeSpan.FromSeconds(_media.Duration.TotalSeconds * ratio));
            _scrubbing = false;
            if (!ProgressHit.IsMouseOver) SetProgressThickness(3);
            UpdateProgress();
        }

        private void ScrubTo(double x)
        {
            double w = ProgressTrack.ActualWidth;
            _scrubRatio = Math.Clamp(x / w, 0, 1);
            ProgressFill.Width = w * _scrubRatio;
            PositionText.Text = FormatTime(TimeSpan.FromSeconds(_media.Duration.TotalSeconds * _scrubRatio));
        }

        private void Progress_MouseEnter(object sender, MouseEventArgs e) => SetProgressThickness(7);
        private void Progress_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_scrubbing) SetProgressThickness(3);
        }

        private void SetProgressThickness(double h)
        {
            var anim = new DoubleAnimation(h, TimeSpan.FromMilliseconds(140));
            ProgressTrack.BeginAnimation(HeightProperty, anim);
            ProgressFill.BeginAnimation(HeightProperty, anim);
            ProgressTrack.CornerRadius = ProgressFill.CornerRadius = new CornerRadius(Math.Min(2, h / 2));
        }

        private static string FormatTime(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        // ───────────── Mídia ─────────────

        private void UpdateMedia()
        {
            bool has = _media.HasSession && (_media.Title.Length > 0 || _media.IsPlaying);
            MediaDetails.Visibility = has ? Visibility.Visible : Visibility.Hidden;
            NoMediaPanel.Visibility = has ? Visibility.Collapsed : Visibility.Visible;

            TitleText.Text = string.IsNullOrEmpty(_media.Title) ? L.T("Sem título") : _media.Title;
            ArtistText.Text = _media.Artist;
            SourceText.Text = _media.SourceApp.ToUpperInvariant();
            ArtBrush.ImageSource = _media.Art;
            MiniArtBrush.ImageSource = _media.Art;
            ArtFallback.Visibility = _media.Art == null ? Visibility.Visible : Visibility.Collapsed;
            ArtGlow.Visibility = _media.Art == null ? Visibility.Hidden : Visibility.Visible;
            PlayPauseGlyph.Data = _media.IsPlaying ? GlyphPause : GlyphPlay;
            UpdateLikeUi();
            UpdateShuffleRepeatUi();
            UpdateAmbient();

            // Pausado, a capa encolhe um pouco; tocando, volta com mola (como num toca-discos que "respira").
            double artTo = _media.IsPlaying || !has ? 1 : 0.9;
            if (Math.Abs(artTo - _artScaleTarget) > 0.001)
            {
                _artScaleTarget = artTo;
                var spring = new DoubleAnimation(artTo, TimeSpan.FromMilliseconds(ReduceMotion ? 0 : 520)) { EasingFunction = new SpringEase() };
                ArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
                ArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
                ArtGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(artTo >= 1 ? 0.5 : 0.2, TimeSpan.FromMilliseconds(400)));
            }

            WatchTrack();
            UpdateCollapsedState();
            if (_expanded) UpdateProgress();
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();

        // Curtir: o Spotify não diz se a faixa já está curtida, então lembramos o que foi feito
        // nesta faixa (o atalho alterna curtir/descurtir).
        private string _likedTrackKey = "";

        private void UpdateLikeUi()
        {
            bool show = _settings.EnableLike && _media.HasSession && LikeService.IsSpotify(_media.SourceAppId);
            LikeButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            bool liked = _likedTrackKey.Length > 0 && _likedTrackKey == _media.Title + "\n" + _media.Artist;
            LikeGlyph.Filled = liked;
            LikeButton.Foreground = liked ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x6D)) : (Brush)FindResource("Ink_A6");
        }

        private void Like_Click(object sender, RoutedEventArgs e)
        {
            string key = _media.Title + "\n" + _media.Artist;
            if (!LikeService.ToggleSpotifyLike()) return;
            _likedTrackKey = _likedTrackKey == key ? "" : key;
            UpdateLikeUi();
            // Pequeno "pulo" no coração para confirmar.
            var pop = new DoubleAnimation(1.25, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.6 } };
            var scale = new ScaleTransform(1, 1, 8, 8);
            LikeGlyph.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        // Aleatório e repetir: pelos controles de mídia do Windows quando o app aceita; no Spotify,
        // que não aceita por ali, pelos atalhos do próprio app (aí o Ilha lembra o estado que pediu).
        private bool _shuffleGuess;
        private int _repeatGuess;

        private bool ShuffleActive => _media.CanShuffle ? _media.ShuffleOn : _shuffleGuess;
        private int RepeatActive => _media.CanRepeat ? _media.RepeatMode : _repeatGuess;

        private async void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            if (_media.CanShuffle) await _media.SetShuffleAsync(!_media.ShuffleOn);
            else if (LikeService.IsSpotify(_media.SourceAppId) && LikeService.ToggleSpotifyShuffle()) _shuffleGuess = !_shuffleGuess;
            UpdateShuffleRepeatUi();
            Pop(ShuffleButton);
        }

        private async void Repeat_Click(object sender, RoutedEventArgs e)
        {
            int next = (RepeatActive + 1) % 3; // desligado → lista → música
            if (_media.CanRepeat) await _media.SetRepeatAsync(next);
            else if (LikeService.IsSpotify(_media.SourceAppId) && LikeService.CycleSpotifyRepeat()) _repeatGuess = next;
            UpdateShuffleRepeatUi();
            Pop(RepeatButton);
        }

        private void UpdateShuffleRepeatUi()
        {
            bool spotify = LikeService.IsSpotify(_media.SourceAppId);
            bool on = _settings.ShowShuffleRepeat && _media.HasSession;
            ShuffleButton.Visibility = on && (_media.CanShuffle || spotify) ? Visibility.Visible : Visibility.Collapsed;
            RepeatButton.Visibility = on && (_media.CanRepeat || spotify) ? Visibility.Visible : Visibility.Collapsed;
            bool shuffle = ShuffleActive;
            int repeat = RepeatActive;
            ShuffleButton.SetResourceReference(ForegroundProperty, shuffle ? "Accent" : "Ink_73");
            ShuffleDot.Visibility = shuffle ? Visibility.Visible : Visibility.Hidden;
            RepeatButton.SetResourceReference(ForegroundProperty, repeat > 0 ? "Accent" : "Ink_73");
            RepeatDot.Visibility = repeat > 0 ? Visibility.Visible : Visibility.Hidden;
            RepeatGlyph.Data = (Geometry)FindResource(repeat == 2 ? "IcoRepeatOne" : "IcoRepeat");
            ShuffleButton.ToolTip = L.T(shuffle ? "Aleatório: ligado" : "Aleatório: desligado");
            RepeatButton.ToolTip = L.T(repeat == 2 ? "Repetir: esta música" : repeat == 1 ? "Repetir: playlist" : "Repetir: desligado");
        }

        /// <summary>Pulinho rápido no botão para confirmar o toque.</summary>
        private static void Pop(FrameworkElement element)
        {
            if (ReduceMotion) return;
            var scale = new ScaleTransform(1, 1);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = scale;
            var pop = new DoubleAnimation(1.2, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.6 } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
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
            if (Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase) Native.SetNoActivate(_hwnd, true);
        }

        private void TextInput_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // O clique já ativou a janela (WS_EX_NOACTIVATE está desligado sobre as áreas de texto);
            // aqui só garantimos o foco do teclado na caixa.
            if (sender is System.Windows.Controls.Primitives.TextBoxBase box && !box.IsKeyboardFocusWithin) box.Focus();
        }

        private void NotesBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            ReleaseKeyboard();
            RestartCollapseTimer();
        }

        /// <summary>Sai da edição das notas e devolve o foco ao app que estava em uso antes.</summary>
        private void ReleaseKeyboard()
        {
            if (_hwnd == IntPtr.Zero) return;
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) Keyboard.ClearFocus();
            if (IsForeground && _previousForeground != IntPtr.Zero && Native.IsWindow(_previousForeground))
                Native.SetForegroundWindow(_previousForeground);
            _previousForeground = IntPtr.Zero;
            if (!PageNotes.IsMouseOver && !TodoPanel.IsMouseOver && !PageTasks.IsMouseOver && !PagePrompter.IsMouseOver)
                Native.SetNoActivate(_hwnd, true);
        }

        // ───────────── Tarefas ─────────────

        private void SaveTodos()
        {
            _settings.Todos = _todos.ToList();
            _settings.Save();
            // O calendário mostra um pontinho nos dias com lembrete pendente.
            UpdateCalendar(force: true);
        }

        private void UpdateTodoSummary()
        {
            var shown = _todos.Where(t => TodoVisible(t)).ToList();
            int open = shown.Count(t => !t.Done);
            TodoCount.Text = shown.Count == 0 ? "" : open == 0 ? L.T("TUDO FEITO") : L.F("{0} A FAZER", open);
            TodoEmpty.Text = L.T("Nada marcado para este dia.");
            QueueDynamicResize();
            TodoEmpty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddTodo()
        {
            string text = TodoInput.Text.Trim();
            if (text.Length == 0) return;
            _todos.Add(new TodoItem { Text = text, Date = DayKey(_selectedDate) });
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
            TimerMode.Text = L.T("TIMER");
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
            TimerMode.Text = L.T(parts.Length > 1 ? parts[1] : "TIMER");
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
                // Fica verde até a pessoa dar OK (ver MainWindow.Alarm.cs).
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
            TimerStartText.Text = L.T(TimerRunning ? "Pausar" : remaining < _timerPreset && remaining > TimeSpan.Zero ? "Continuar" : "Iniciar");
            double total = Math.Max(_timerPreset.TotalSeconds, TimerRemaining.TotalSeconds);
            DrawTimerRing(total <= 0 ? 0 : TimerRemaining.TotalSeconds / total);
            TimerStatus.Text = L.T(_timerAlerting ? "Tempo esgotado!"
                : TimerRunning ? "Em andamento"
                : remaining < _timerPreset && remaining > TimeSpan.Zero ? "Pausado"
                : "Pronto");
            TimerStatus.Foreground = (Brush)FindResource(_timerAlerting ? "Accent" : "Secondary");
            UpdateCollapsedState();
        }

        // Contorno do timer: quadrado de cantos suaves, começando no meio do topo, sentido horário.
        private const double TimerSize = 156, TimerStroke = 4, TimerCorner = 40;
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
            StartAlarmSound();

            if (_hiddenForFullscreen)
            {
                _hiddenForFullscreen = false;
                _glassWindow?.Show();
                Show();
                PositionOnScreen();
            }
            if (_settings.ShowTimer) TabTimer.IsChecked = true;
            ShowAlertCard();
            UpdateTimerUi();
        }

        private void StopAlert()
        {
            if (!_timerAlerting) return;
            _timerAlerting = false;
            StopAlarmSound();
            HideAlertCard();
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
                ClipboardStatus.Text = L.T("Copiado! ✓");
            }
            catch
            {
                _ignoreNextClipboard = false;
                ClipboardStatus.Text = L.T("Não foi possível copiar agora");
            }

            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            reset.Tick += (_, _) =>
            {
                reset.Stop();
                ClipboardStatus.Text = L.T("Clique em um item para copiar de novo");
            };
            reset.Start();
        }

        private void ClipboardClear_Click(object sender, RoutedEventArgs e) => _clips.Clear();
    }
}
