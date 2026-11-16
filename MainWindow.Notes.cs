using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Ilha
{
    /// <summary>
    /// Notas em páginas (como no Notion): a lista mostra cada página como um bloco da sua cor;
    /// o "+" cria uma nova e, dentro dela, dá para usar negrito, itálico e sublinhado.
    /// </summary>
    public partial class MainWindow
    {
        private NotePage? _openNote;
        private bool _loadingNote;
        private bool _deleteArmed;
        private readonly DispatcherTimer _deleteDisarm = new() { Interval = TimeSpan.FromSeconds(3) };

        private void InitNotes()
        {
            // Colar traz só o texto (sem cores e fontes de outros apps).
            DataObject.AddPastingHandler(NoteBody, (_, e) =>
            {
                if (e.DataObject.GetData(DataFormats.UnicodeText) is string text)
                {
                    var plain = new DataObject();
                    plain.SetData(DataFormats.UnicodeText, text);
                    e.DataObject = plain;
                }
                else
                {
                    e.CancelCommand();
                }
            });
            var doc = NoteBody.Document;
            doc.PagePadding = new Thickness(0);
            doc.SetResourceReference(FlowDocument.FontFamilyProperty, "UiFont");
            doc.SetResourceReference(FlowDocument.ForegroundProperty, "Ink_FF");
            doc.FontSize = 13.5;
            NoteBody.Resources[typeof(Paragraph)] = new Style(typeof(Paragraph))
            {
                Setters = { new Setter(Block.MarginProperty, new Thickness(0, 0, 0, 2)) },
            };
            _deleteDisarm.Tick += (_, _) => DisarmDelete();
            NotePagesScroll.SizeChanged += (_, e) => { if (e.WidthChanged) RebuildNotePages(); };
            BuildNoteColors();
            RebuildNotePages();
        }

        // ───── Lista de páginas ─────

        private void RebuildNotePages()
        {
            NotePagesPanel.Children.Clear();
            var pages = _settings.NotePages.OrderByDescending(p => p.Updated).ToList();
            NotesEmpty.Visibility = pages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            QueueDynamicResize();
            double avail = NotePagesScroll.ActualWidth;
            if (avail <= 0) return;
            int cols = avail >= 330 ? 2 : 1;
            const double gap = 8;
            double w = Math.Floor((avail - (cols - 1) * gap) / cols);
            for (int i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var color = NotePage.ColorOf(page.Color);
                var mark = new Border
                {
                    Width = 10, Height = 10,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(color),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                DockPanel.SetDock(mark, Dock.Left);
                var title = new TextBlock
                {
                    Text = page.Title.Length > 0 ? page.Title : L.T("Sem título"),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                var preview = new TextBlock
                {
                    Text = page.Preview.Length > 0 ? page.Preview : L.T("Página vazia"),
                    FontSize = 11.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(18, 3, 0, 0),
                };
                preview.SetResourceReference(TextBlock.ForegroundProperty, "Tertiary");
                var idle = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B));
                var hover = new SolidColorBrush(Color.FromArgb(0x3D, color.R, color.G, color.B));
                var card = new Border
                {
                    Width = w,
                    Margin = new Thickness(0, 0, i % cols < cols - 1 ? gap : 0, gap),
                    Padding = new Thickness(12, 10, 12, 10),
                    CornerRadius = new CornerRadius(12),
                    Background = idle,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Child = new StackPanel
                    {
                        Children = { new DockPanel { Children = { mark, title } }, preview },
                    },
                };
                card.MouseEnter += (_, _) => card.Background = hover;
                card.MouseLeave += (_, _) => card.Background = idle;
                card.MouseLeftButtonUp += (_, _) => OpenNote(page);
                NotePagesPanel.Children.Add(card);
            }
        }

        private void NewNotePage_Click(object sender, RoutedEventArgs e)
        {
            // Cada página nova começa com a cor seguinte (dá para trocar logo embaixo).
            var color = NotePage.Colors[_settings.NotePages.Count % NotePage.Colors.Count].Key;
            var page = new NotePage { Color = color };
            _settings.NotePages.Add(page);
            _settings.Save();
            OpenNote(page);
            Dispatcher.BeginInvoke(() => NoteTitle.Focus(), DispatcherPriority.Input);
        }

        // ───── Página aberta ─────

        private void OpenNote(NotePage page)
        {
            SaveOpenNote();
            DisarmDelete();
            _openNote = page;
            _loadingNote = true;
            NoteTitle.Text = page.Title;
            NoteText.Load(NoteBody.Document, page.Content);
            _loadingNote = false;
            UpdateNotePlaceholders();
            UpdateNoteChrome();
            SwapNoteViews(NoteEditor, NotesListView, forward: true);
            NoteBody.CaretPosition = NoteBody.Document.ContentEnd;
            QueueDynamicResize();
        }

        private void NoteBack_Click(object sender, RoutedEventArgs e) => CloseNote();

        private void CloseNote()
        {
            if (_openNote == null) return;
            SaveOpenNote();
            var page = _openNote;
            _openNote = null;
            DisarmDelete();
            // Página que ficou vazia (sem título e sem texto) não fica na lista.
            if (page.Title.Length == 0 && NoteText.ToPlain(page.Content).Trim().Length == 0)
            {
                _settings.NotePages.Remove(page);
                _settings.Save();
            }
            if (NoteEditor.IsKeyboardFocusWithin) Keyboard.ClearFocus();
            RebuildNotePages();
            SwapNoteViews(NotesListView, NoteEditor, forward: false);
        }

        /// <summary>Troca entre a lista e a página com um deslize curto.</summary>
        private void SwapNoteViews(FrameworkElement show, FrameworkElement hide, bool forward)
        {
            hide.Visibility = Visibility.Collapsed;
            show.Visibility = Visibility.Visible;
            if (ReduceMotion || !_expanded) return;
            var move = new TranslateTransform(forward ? 16 : -16, 0);
            show.RenderTransform = move;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            show.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        }

        /// <summary>Grava o que está aberto (título, texto e um resumo para a lista).</summary>
        private void SaveOpenNote()
        {
            _notesSave.Stop();
            if (_openNote is not { } page) return;
            string content = NoteText.Save(NoteBody.Document);
            string title = NoteTitle.Text.Trim();
            if (content == page.Content && title == page.Title) return;
            page.Content = content;
            page.Title = title;
            page.Preview = NoteText.PreviewOf(NoteText.ToPlain(content));
            page.Updated = DateTime.Now;
            _settings.Save();
        }

        private void NoteBody_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingNote) return;
            UpdateNotePlaceholders();
            _notesSave.Stop();
            _notesSave.Start();
        }

        private void NoteTitle_TextChanged(object sender, TextChangedEventArgs e)
        {
            NoteTitlePlaceholder.Visibility = NoteTitle.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_loadingNote) return;
            _notesSave.Stop();
            _notesSave.Start();
        }

        private void NoteTitle_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Enter or Key.Down)
            {
                e.Handled = true;
                NoteBody.Focus();
                NoteBody.CaretPosition = NoteBody.Document.ContentStart;
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ReleaseKeyboard();
                RestartCollapseTimer();
            }
        }

        private void UpdateNotePlaceholders()
        {
            NoteTitlePlaceholder.Visibility = NoteTitle.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var doc = NoteBody.Document;
            bool empty = new TextRange(doc.ContentStart, doc.ContentEnd).Text.Trim().Length == 0;
            NoteBodyPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        // ───── Cores ─────

        private void BuildNoteColors()
        {
            NoteColors.Children.Clear();
            foreach (var (key, name, value) in NotePage.Colors)
            {
                var ring = new Border
                {
                    Width = 20, Height = 20,
                    CornerRadius = new CornerRadius(7),
                    BorderThickness = new Thickness(1.5),
                    BorderBrush = Brushes.Transparent,
                    Background = Brushes.Transparent,
                    Margin = new Thickness(0, 0, 2, 0),
                    Cursor = Cursors.Hand,
                    Tag = key,
                    ToolTip = L.T(name),
                    Child = new Border
                    {
                        Width = 12, Height = 12,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(value),
                    },
                };
                ring.MouseLeftButtonUp += (_, _) =>
                {
                    if (_openNote == null) return;
                    _openNote.Color = key;
                    _openNote.Updated = DateTime.Now;
                    _settings.Save();
                    UpdateNoteChrome();
                };
                NoteColors.Children.Add(ring);
            }
        }

        private void UpdateNoteChrome()
        {
            string key = _openNote?.Color ?? "blue";
            NoteColorMark.Background = new SolidColorBrush(NotePage.ColorOf(key));
            foreach (var ring in NoteColors.Children.OfType<Border>())
            {
                if ((string)ring.Tag == key) ring.SetResourceReference(Border.BorderBrushProperty, "Ink_BF");
                else ring.BorderBrush = Brushes.Transparent;
            }
            UpdateFormatButtons();
        }

        // ───── Negrito, itálico, sublinhado ─────

        private void NoteFormat_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag }) return;
            if (!NoteBody.IsKeyboardFocusWithin) NoteBody.Focus();
            var command = tag switch
            {
                "b" => EditingCommands.ToggleBold,
                "i" => EditingCommands.ToggleItalic,
                _ => EditingCommands.ToggleUnderline,
            };
            command.Execute(null, NoteBody);
            UpdateFormatButtons();
        }

        private void NoteBody_SelectionChanged(object sender, RoutedEventArgs e) => UpdateFormatButtons();

        private void UpdateFormatButtons()
        {
            var sel = NoteBody.Selection;
            bool bold = sel.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w.ToOpenTypeWeight() >= 600;
            bool italic = sel.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic;
            bool underline = sel.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection d &&
                             d.Any(x => x.Location == TextDecorationLocation.Underline);
            SetFormatActive(BoldButton, bold);
            SetFormatActive(ItalicButton, italic);
            SetFormatActive(UnderlineButton, underline);
        }

        private void SetFormatActive(Button button, bool on)
        {
            if (on) button.SetResourceReference(BackgroundProperty, "Ink_26");
            else button.Background = Brushes.Transparent;
            button.SetResourceReference(ForegroundProperty, on ? "Ink_FF" : "Ink_A6");
        }

        // ───── Apagar (dois cliques, para não apagar sem querer) ─────

        private void NoteDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_openNote == null) return;
            if (!_deleteArmed)
            {
                _deleteArmed = true;
                NoteDeleteButton.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
                NoteDeleteButton.ToolTip = L.T("Clique de novo para apagar");
                _deleteDisarm.Start();
                return;
            }
            var page = _openNote;
            _openNote = null;
            DisarmDelete();
            _settings.NotePages.Remove(page);
            _settings.Save();
            if (NoteEditor.IsKeyboardFocusWithin) Keyboard.ClearFocus();
            RebuildNotePages();
            SwapNoteViews(NotesListView, NoteEditor, forward: false);
        }

        private void DisarmDelete()
        {
            _deleteDisarm.Stop();
            _deleteArmed = false;
            NoteDeleteButton.SetResourceReference(ForegroundProperty, "Ink_73");
            NoteDeleteButton.ToolTip = L.T("Apagar página");
        }
    }
}
