using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace Ilha
{
    /// <summary>
    /// Soltar arquivos na ilha: ela abre e pergunta o que fazer (guardar na prateleira ou converter).
    /// Ao soltar, a ilha dá uma chacoalhada curta e um traço contorna a borda, como um encaixe.
    /// O conversor mostra só o que faz sentido para o arquivo e deixa o resultado ao lado do original.
    /// </summary>
    public partial class MainWindow
    {
        private List<string> _convertFiles = new();
        private List<string> _convertResults = new();
        private bool _converting, _convertDone, _convertPressed;
        private int _convertLoad;
        private Point _convertDragStart;

        private bool _dropActive, _dropHold;
        private string _dropZone = "";
        private readonly DispatcherTimer _dropLeaveTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
        private readonly DispatcherTimer _dropHoldTimer = new() { Interval = TimeSpan.FromSeconds(3) };

        private bool _connectActive;
        private EventHandler? _shakeFollow;

        private void InitConvert()
        {
            _dropLeaveTimer.Tick += (_, _) =>
            {
                _dropLeaveTimer.Stop();
                ShowDropOverlay(false);
                if (!_draggingOut) RestartCollapseTimer();
            };
            _dropHoldTimer.Tick += (_, _) =>
            {
                _dropHoldTimer.Stop();
                _dropHold = false;
                if (!ShouldStayOpen()) RestartCollapseTimer();
            };
            Island.SizeChanged += (_, _) =>
            {
                if (_connectActive) ConnectStroke.Data = ConnectGeometry(out _);
            };
        }

        // ───────────── Arrastar para a ilha ─────────────

        private bool DropAccepted(DragEventArgs e) =>
            !_draggingOut && (_settings.ShowShelf || _settings.ShowConverter) && e.Data.GetDataPresent(DataFormats.FileDrop);

        /// <summary>Metade esquerda guarda, metade direita converte (ou a ilha inteira, se só um estiver ligado).</summary>
        private string DropZoneAt(DragEventArgs e)
        {
            if (!_settings.ShowConverter) return "shelf";
            if (!_settings.ShowShelf) return "convert";
            if (DropOverlay.ActualWidth <= 0) return "shelf";
            return e.GetPosition(DropOverlay).X < DropOverlay.ActualWidth / 2 ? "shelf" : "convert";
        }

        private void Island_DragOver(object sender, DragEventArgs e)
        {
            bool accept = DropAccepted(e);
            e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            if (!accept) return;
            _collapseTimer.Stop();
            _dropLeaveTimer.Stop();
            Expand();
            ShowDropOverlay(true);
            SetDropZone(DropZoneAt(e));
        }

        private void Island_DragLeave(object sender, DragEventArgs e)
        {
            // Passar de um elemento para outro dentro da ilha também dispara "saiu": espera um instante.
            _dropLeaveTimer.Stop();
            _dropLeaveTimer.Start();
        }

        private void Island_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            _dropLeaveTimer.Stop();
            bool accept = DropAccepted(e);
            string zone = DropZoneAt(e);
            ShowDropOverlay(false);
            if (!accept || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

            HoldOpen();
            Expand();
            if (zone == "convert")
            {
                TabConvert.IsChecked = true;
                LoadConvert(paths);
            }
            else
            {
                TabShelf.IsChecked = true;
                foreach (var path in paths)
                {
                    if (_shelf.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                    var item = ShelfItem.From(path);
                    if (item.Exists()) _shelf.Add(item);
                }
            }
            PlayConnect(shake: true);
        }

        /// <summary>Depois de soltar, a ilha fica aberta um pouco (o Windows demora a avisar onde o mouse está).</summary>
        private void HoldOpen()
        {
            _dropHold = true;
            _collapseTimer.Stop();
            _dropHoldTimer.Stop();
            _dropHoldTimer.Start();
        }

        private void ShowDropOverlay(bool on)
        {
            if (on == _dropActive) return;
            _dropActive = on;
            if (on)
            {
                bool shelf = _settings.ShowShelf, convert = _settings.ShowConverter;
                DropShelf.Visibility = shelf ? Visibility.Visible : Visibility.Collapsed;
                DropConvert.Visibility = convert ? Visibility.Visible : Visibility.Collapsed;
                DropShelfCol.Width = shelf ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                DropConvertCol.Width = convert ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                DropGapCol.Width = new GridLength(shelf && convert ? 12 : 0);
                _dropZone = "";
            }
            Fade(DropOverlay, on ? 1 : 0, on ? 180 : 120, delayMs: on ? 60 : 0);
            Fade(ContentRoot, on ? 0 : 1, on ? 110 : 200, delayMs: on ? 0 : 60);
        }

        private void SetDropZone(string zone)
        {
            if (zone == _dropZone) return;
            _dropZone = zone;
            Light(DropShelf, DropShelfGlyph, DropShelfText, zone == "shelf");
            Light(DropConvert, DropConvertGlyph, DropConvertText, zone == "convert");

            void Light(Border card, Glyph glyph, TextBlock text, bool on)
            {
                card.SetResourceReference(Border.BorderBrushProperty, on ? "Accent" : "CardStroke");
                card.BorderThickness = new Thickness(on ? 1.5 : 1);
                glyph.SetResourceReference(Glyph.ForegroundProperty, on ? "Ink_FF" : "Ink_73");
                text.SetResourceReference(TextBlock.ForegroundProperty, on ? "Ink_FF" : "Tertiary");
                card.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0.6, TimeSpan.FromMilliseconds(140)));
                if (on && !ReduceMotion)
                {
                    // O cartão escolhido dá um pulinho curto ao receber o cursor.
                    var scale = new ScaleTransform(1, 1);
                    card.RenderTransformOrigin = new Point(0.5, 0.5);
                    card.RenderTransform = scale;
                    var pop = new DoubleAnimation(0.975, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new SpringEase { Damping = 0.7, Frequency = 9 } };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                }
            }
        }

        // ───────────── Encaixe: chacoalhada + traço em volta ─────────────

        /// <summary>
        /// Contorno da ilha em duas metades que nascem no meio da borda de baixo e sobem pelos lados
        /// (na ilha solta, fecham em cima). Desenhadas juntas, parecem "abraçar" a ilha.
        /// </summary>
        private Geometry ConnectGeometry(out double halfLength)
        {
            double w = Island.ActualWidth, h = Island.ActualHeight;
            const double inset = 0.9;
            bool closed = Detached;
            var corners = Island.CornerRadius;
            double limit = Math.Max(0, Math.Min(w, h) / 2 - inset);
            double rb = Math.Min(Math.Max(0, corners.BottomRight - inset), limit);
            double rt = Math.Min(Math.Max(0, corners.TopRight - inset), limit);
            double left = inset, right = w - inset, top = closed ? inset : 0, bottom = h - inset, cx = w / 2;

            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                foreach (int side in new[] { 1, -1 })
                {
                    double edge = side > 0 ? right : left;
                    var sweep = side > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
                    g.BeginFigure(new Point(cx, bottom), false, false);
                    g.LineTo(new Point(edge - side * rb, bottom), true, true);
                    if (rb > 0) g.ArcTo(new Point(edge, bottom - rb), new Size(rb, rb), 0, false, sweep, true, true);
                    g.LineTo(new Point(edge, top + rt), true, true);
                    if (rt > 0) g.ArcTo(new Point(edge - side * rt, top), new Size(rt, rt), 0, false, sweep, true, true);
                    if (closed) g.LineTo(new Point(cx, top), true, true);
                }
            }
            geometry.Freeze();
            halfLength = Math.Max(0, cx - inset - rb) + Math.PI * rb / 2 + Math.Max(0, bottom - rb - top - rt) +
                         Math.PI * rt / 2 + (closed ? Math.Max(0, cx - inset - rt) : 0);
            return geometry;
        }

        private void PlayConnect(bool shake)
        {
            if (_hiddenForFullscreen) return;
            ConnectStroke.Data = ConnectGeometry(out double length);
            // A ilha pode estar mudando de tamanho (troca de aba): o traço precisa cobrir o tamanho final.
            if (_expanded) length = Math.Max(length, _expW + _expH);
            double units = Math.Max(1, length / ConnectStroke.StrokeThickness) + 2;
            _connectActive = true;

            if (ReduceMotion)
            {
                ConnectStroke.StrokeDashArray = null;
                var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) };
                blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.15)));
                blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.6)));
                blink.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                blink.Completed += (_, _) => _connectActive = false;
                ConnectStroke.BeginAnimation(OpacityProperty, blink);
                return;
            }

            ConnectStroke.StrokeDashArray = new DoubleCollection { units, units * 2 };
            var draw = new DoubleAnimation(units, 0, TimeSpan.FromMilliseconds(640))
            {
                BeginTime = TimeSpan.FromMilliseconds(shake ? 90 : 0),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            ConnectStroke.StrokeDashOffset = units;
            ConnectStroke.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, draw);

            var glow = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1500) };
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.08)));
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.62)));
            glow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
            glow.Completed += (_, _) => _connectActive = false;
            ConnectStroke.BeginAnimation(OpacityProperty, glow);

            if (!shake) return;
            // Chacoalhada: vai e volta cada vez menor, como algo que encaixou no lugar.
            var wobble = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            (double X, int Ms)[] steps = { (-5, 55), (4.2, 120), (-3.2, 185), (2.2, 250), (-1.2, 310), (0, 380) };
            foreach (var (x, ms) in steps)
                wobble.KeyFrames.Add(new EasingDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)),
                    new SineEase { EasingMode = EasingMode.EaseInOut }));
            if (_shakeFollow == null)
            {
                // O vidro de trás (outra janela) acompanha o movimento quadro a quadro.
                _shakeFollow = (_, _) => UpdateGlassRegion();
                CompositionTarget.Rendering += _shakeFollow;
            }
            wobble.Completed += (_, _) =>
            {
                if (_shakeFollow != null) CompositionTarget.Rendering -= _shakeFollow;
                _shakeFollow = null;
                UpdateGlassRegion();
            };
            IslandShake.BeginAnimation(TranslateTransform.XProperty, wobble);
        }

        // ───────────── Conversor ─────────────

        private void ShowConvertState(string state)
        {
            ConvertEmpty.Visibility = state == "empty" ? Visibility.Visible : Visibility.Collapsed;
            ConvertBody.Visibility = state == "empty" ? Visibility.Collapsed : Visibility.Visible;
            ConvertOptions.Visibility = state == "options" ? Visibility.Visible : Visibility.Collapsed;
            ConvertWork.Visibility = state == "work" ? Visibility.Visible : Visibility.Collapsed;
            ConvertDone.Visibility = state == "done" ? Visibility.Visible : Visibility.Collapsed;
            ConvertClose.Visibility = state == "work" ? Visibility.Hidden : Visibility.Visible;
            _convertDone = state == "done";
            ConvertFileRow.Cursor = _convertDone ? Cursors.Hand : null;
            ConvertFileRow.ToolTip = _convertDone ? L.T("Arraste para onde quiser") : null;
            if (state != "done")
            {
                ConvertCheckBox.BeginAnimation(OpacityProperty, null);
                ConvertCheckBox.Opacity = 0;
                ConvertIcon.BeginAnimation(OpacityProperty, null);
                ConvertIcon.Opacity = 1;
            }
        }

        private static string KindLabel(string path) => Converter.KindOf(path) switch
        {
            Converter.Kind.Image => L.T("Imagem"),
            Converter.Kind.Pdf => "PDF",
            Converter.Kind.Audio => L.T("Áudio"),
            Converter.Kind.Video => L.T("Vídeo"),
            _ => System.IO.Directory.Exists(path) ? L.T("Pasta") : L.T("Arquivo"),
        };

        private static string FileLabel(string path) => IOPath.GetFileName(path.TrimEnd('\\', '/'));

        private async void LoadConvert(IEnumerable<string> paths)
        {
            if (_converting) return;
            int load = ++_convertLoad;
            _convertFiles = paths.Where(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p)).ToList();
            _convertResults = new List<string>();
            if (_convertFiles.Count == 0)
            {
                ShowConvertState("empty");
                return;
            }

            var files = _convertFiles;
            string first = files[0];
            ConvertIcon.Source = Native.GetShellIcon(first);
            ConvertName.Text = files.Count == 1 ? FileLabel(first) : L.F("{0} arquivos", files.Count);
            string kind = files.Select(KindLabel).Distinct().Count() == 1 ? KindLabel(first) : L.T("Vários tipos");
            ConvertInfo.Text = kind;

            ConvertOptions.Children.Clear();
            var options = Converter.OptionsFor(files);
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var chip = new Button
                {
                    Style = (Style)FindResource("ChipButton"),
                    Content = option.Label,
                    ToolTip = option.Hint,
                    Tag = option.Id,
                    Padding = new Thickness(13, 6, 13, 6),
                };
                chip.Click += ConvertOption_Click;
                ConvertOptions.Children.Add(chip);
                if (ReduceMotion) continue;
                // As opções chegam uma atrás da outra, subindo um pouco.
                var rise = new TranslateTransform(0, 8);
                chip.RenderTransform = rise;
                var delay = TimeSpan.FromMilliseconds(120 + i * 45);
                chip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = delay });
                rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(420))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            }
            ShowConvertState("options");

            // O tamanho pode demorar (pasta grande): entra depois, sem travar a ilha.
            long size = await Task.Run(() => files.Sum(Converter.SizeOf));
            if (load == _convertLoad && !_converting && !_convertDone && size > 0)
                ConvertInfo.Text = $"{kind} · {Converter.SizeText(size)}";
        }

        private async void ConvertOption_Click(object sender, RoutedEventArgs e)
        {
            if (_converting || (sender as FrameworkElement)?.Tag is not string id || _convertFiles.Count == 0) return;
            _converting = true;
            var files = _convertFiles.ToList();
            string label = (sender as Button)?.Content as string ?? "";
            ShowConvertState("work");
            ConvertWorkText.Text = id switch
            {
                "compress" or "video-small" => L.T("Comprimindo…"),
                "zip" => L.T("Compactando…"),
                "half" => L.T("Reduzindo…"),
                "mp3" when Converter.KindOf(files[0]) == Converter.Kind.Video => L.T("Extraindo o áudio…"),
                _ => L.F("Convertendo para {0}…", label),
            };
            SetConvertBar(0, animate: false);
            var started = DateTime.Now;
            try
            {
                long before = await Task.Run(() => files.Sum(Converter.SizeOf));
                var progress = new Progress<double>(p => SetConvertBar(p, animate: true));
                var results = await Converter.RunAsync(id, files, progress);
                SetConvertBar(1, animate: true);
                // Conversões instantâneas ainda mostram a barra enchendo, em vez de piscar.
                int wait = 520 - (int)(DateTime.Now - started).TotalMilliseconds;
                if (wait > 0) await Task.Delay(wait);
                _converting = false;
                ShowConvertResult(results, before);
            }
            catch (ConvertException ex)
            {
                _converting = false;
                ShowConvertState("options");
                ConvertInfo.Text = ex.Message;
            }
            finally
            {
                _converting = false;
            }
        }

        private void SetConvertBar(double fraction, bool animate)
        {
            double width = Math.Max(0, ConvertTrack.ActualWidth) * Math.Clamp(fraction, 0, 1);
            if (!animate)
            {
                ConvertBar.BeginAnimation(WidthProperty, null);
                ConvertBar.Width = width;
                return;
            }
            ConvertBar.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }

        private async void ShowConvertResult(List<string> results, long before)
        {
            _convertResults = results;
            if (results.Count == 0)
            {
                ShowConvertState("options");
                return;
            }
            string first = results[0];
            long after = await Task.Run(() => results.Sum(Converter.SizeOf));
            ConvertIcon.Source = Native.GetShellIcon(first);
            ConvertName.Text = results.Count == 1 ? FileLabel(first) : L.F("{0} arquivos prontos", results.Count);
            string folder = FileLabel(IOPath.GetDirectoryName(first.TrimEnd('\\', '/')) ?? "");
            string sizes = before > 0 && after > 0 && after < before * 0.97
                ? L.F("{0} → {1}", Converter.SizeText(before), Converter.SizeText(after))
                : Converter.SizeText(after);
            ConvertInfo.Text = folder.Length > 0 ? L.F("{0} · em {1}", sizes, folder) : sizes;
            ConvertShelfButton.Visibility = _settings.ShowShelf ? Visibility.Visible : Visibility.Collapsed;
            ShowConvertState("done");

            if (!_expanded)
            {
                // A ilha já tinha fechado (conversão longa): avisa que terminou.
                ShowPeek((Geometry)FindResource("IcoCheck"), false, null, null, L.T("Conversão pronta"), null, "",
                         (Brush)FindResource("Accent"), seconds: 3.5);
                return;
            }
            HoldOpen();
            PlayConnect(shake: false);
            if (ReduceMotion) return;

            // O "feito" se desenha no lugar do ícone e depois dá lugar ao arquivo novo.
            ConvertCheck.StrokeDashArray = new DoubleCollection { 8, 16 };
            ConvertCheck.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(420))
                {
                    BeginTime = TimeSpan.FromMilliseconds(80),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            var check = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1500) };
            check.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
            check.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.75)));
            check.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            ConvertCheckBox.BeginAnimation(OpacityProperty, check);
            var icon = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1500) };
            icon.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            icon.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.75)));
            icon.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(1)));
            ConvertIcon.BeginAnimation(OpacityProperty, icon);
        }

        private void ConvertClear_Click(object sender, RoutedEventArgs e)
        {
            if (_converting) return;
            _convertLoad++;
            _convertFiles = new List<string>();
            _convertResults = new List<string>();
            ShowConvertState("empty");
        }

        private void ConvertAgain_Click(object sender, RoutedEventArgs e) => LoadConvert(_convertFiles.ToList());

        private void ConvertReveal_Click(object sender, RoutedEventArgs e)
        {
            if (_convertResults.Count == 0) return;
            string path = _convertResults[0];
            try
            {
                if (System.IO.Directory.Exists(path)) OpenPath(path);
                else Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            catch
            {
                // Sem o Explorador disponível: nada a fazer.
            }
        }

        private void ConvertToShelf_Click(object sender, RoutedEventArgs e)
        {
            foreach (var path in _convertResults)
            {
                if (_shelf.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                var item = ShelfItem.From(path);
                if (item.Exists()) _shelf.Add(item);
            }
            TabShelf.IsChecked = true;
        }

        // Resultado pronto: a linha do arquivo pode ser arrastada para qualquer pasta ou app.

        private void ConvertRow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _convertDragStart = e.GetPosition(this);
            _convertPressed = _convertDone;
        }

        private void ConvertRow_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_convertPressed || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _convertDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - _convertDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _convertPressed = false;
            var files = _convertResults.Where(f => System.IO.File.Exists(f) || System.IO.Directory.Exists(f)).ToArray();
            if (files.Length == 0) return;
            _draggingOut = true;
            try
            {
                DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DataFormats.FileDrop, files),
                    DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            finally
            {
                _draggingOut = false;
            }
            RestartCollapseTimer();
        }
    }
}
