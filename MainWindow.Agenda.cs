using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ilha
{
    /// <summary>
    /// Agenda: os dias passam de lado numa faixa (rolar o mouse ou clicar), o dia escolhido cresce
    /// no centro e, embaixo, ficam os lembretes desse dia.
    /// </summary>
    public partial class MainWindow
    {
        private const double DayCellW = 40, DayCellH = 58;
        private const int DaysBack = 60, DaysAhead = 366;
        private DateTime _stripStart;
        private DateTime _stripBuiltFor;
        private string _stripLang = "";
        private int _wheelAcc;
        private readonly List<DayCell> _dayCells = new();

        private sealed class DayCell
        {
            public DateTime Day;
            public Border Root = null!;
            public TextBlock Week = null!;
            public TextBlock Number = null!;
            public ScaleTransform Scale = null!;
            public Border Dot = null!;
            public bool Selected;
        }

        private void UpdateCalendar(bool force = false)
        {
            var today = DateTime.Today;
            if (!force && today == _calendarDate) return;
            bool dayChanged = today != _calendarDate;
            // Virou o dia com "hoje" escolhido: acompanha o novo hoje.
            if (_selectedDate == _calendarDate) _selectedDate = today;
            _calendarDate = today;
            if (dayChanged) PurgeOldTasks();
            var culture = CultureInfo.CurrentCulture;
            var sel = _selectedDate;

            CalMonth.Text = culture.TextInfo.ToTitleCase(sel.ToString("MMM", culture).TrimEnd('.'));
            CalYear.Text = sel.Year.ToString(culture);
            bool isToday = sel == today;
            CalYear.Visibility = isToday ? Visibility.Visible : Visibility.Hidden;
            TodayButton.Visibility = isToday ? Visibility.Collapsed : Visibility.Visible;

            string day = sel.ToString(L.English ? "dddd, MMMM d" : "dddd, d 'de' MMMM", culture);
            AgendaWeekday.Text = ((isToday ? L.T("Hoje") + " · " : "") + day).ToUpper(culture);
            RefreshTodoFilter();

            if (_dayCells.Count == 0 || _stripBuiltFor != today || _stripLang != L.Lang) BuildDayStrip(today, culture);
            RefreshDayStrip(animate: _expanded && !dayChanged);
        }

        private void BuildDayStrip(DateTime today, CultureInfo culture)
        {
            DayStripItems.Children.Clear();
            _dayCells.Clear();
            _stripStart = today.AddDays(-DaysBack);
            for (int k = 0; k < DaysBack + DaysAhead; k++)
            {
                var d = _stripStart.AddDays(k);
                var week = new TextBlock
                {
                    Text = culture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek).TrimEnd('.').ToUpper(culture),
                    FontSize = 9.5,
                    FontWeight = FontWeights.Medium,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 5),
                };
                var scale = new ScaleTransform(1, 1);
                var number = new TextBlock
                {
                    Text = d.Day.ToString(culture),
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = scale,
                    Typography = { NumeralAlignment = FontNumeralAlignment.Tabular },
                };
                var dot = new Border
                {
                    Width = 4.5, Height = 4.5,
                    CornerRadius = new CornerRadius(2.25),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0),
                    Visibility = Visibility.Hidden,
                };
                var root = new Border
                {
                    Width = DayCellW, Height = DayCellH,
                    CornerRadius = new CornerRadius(10),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { week, number, dot },
                    },
                };
                var cell = new DayCell { Day = d, Root = root, Week = week, Number = number, Scale = scale, Dot = dot };
                root.MouseLeftButtonUp += (_, _) => SelectDay(cell.Day);
                root.MouseEnter += (_, _) => { if (!cell.Selected) root.Background = Ink(0x0F); };
                root.MouseLeave += (_, _) => root.Background = Brushes.Transparent;
                _dayCells.Add(cell);
                DayStripItems.Children.Add(root);
            }
            _stripBuiltFor = today;
            _stripLang = L.Lang;
        }

        /// <summary>Cores, pontinhos de lembrete, o dia escolhido maior e a faixa centralizada nele.</summary>
        private void RefreshDayStrip(bool animate)
        {
            var today = DateTime.Today;
            var sel = _selectedDate;
            var pending = new HashSet<string>(_todos.Where(t => !t.Done)
                .Select(t => string.IsNullOrEmpty(t.Date) ? DayKey(today) : t.Date!));
            bool motion = animate && !ReduceMotion;
            foreach (var c in _dayCells)
            {
                bool isSel = c.Day == sel;
                bool isToday = c.Day == today;
                bool weekend = c.Day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                c.Week.SetResourceReference(TextBlock.ForegroundProperty, isSel ? "Accent" : "Tertiary");
                c.Number.SetResourceReference(TextBlock.ForegroundProperty,
                    isSel ? "Accent" : isToday ? "Ink_FF" : weekend ? "Tertiary" : "Ink_BF");
                c.Number.FontWeight = isSel || isToday ? FontWeights.SemiBold : FontWeights.Normal;
                bool has = pending.Contains(DayKey(c.Day));
                c.Dot.Visibility = has ? Visibility.Visible : Visibility.Hidden;
                c.Dot.SetResourceReference(Border.BackgroundProperty, isSel ? "Accent" : "Ink_73");
                if (isSel != c.Selected)
                {
                    c.Selected = isSel;
                    c.Root.Background = Brushes.Transparent;
                    double to = isSel ? 1.4 : 1;
                    if (motion)
                    {
                        var grow = new DoubleAnimation(to, TimeSpan.FromMilliseconds(isSel ? 460 : 260))
                        {
                            EasingFunction = isSel ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }
                                                   : new CubicEase { EasingMode = EasingMode.EaseOut },
                        };
                        c.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                        c.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                    }
                    else
                    {
                        c.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        c.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        c.Scale.ScaleX = c.Scale.ScaleY = to;
                    }
                }
            }
            CenterDayStrip(motion);
        }

        /// <summary>Desliza a faixa até o dia escolhido ficar no centro.</summary>
        private void CenterDayStrip(bool animate)
        {
            double w = DayStrip.ActualWidth;
            if (w <= 0 || _dayCells.Count == 0) return;
            int index = Math.Clamp((_selectedDate - _stripStart).Days, 0, _dayCells.Count - 1);
            double x = Math.Round(w / 2 - (index * DayCellW + DayCellW / 2));
            if (animate)
            {
                var slide = new DoubleAnimation(x, TimeSpan.FromMilliseconds(_settings.SpringAnimation ? 520 : 340))
                {
                    EasingFunction = _settings.SpringAnimation
                        ? new SpringEase { Damping = 0.86, Frequency = 8 }
                        : new CubicEase { EasingMode = EasingMode.EaseOut },
                };
                DayStripShift.BeginAnimation(TranslateTransform.XProperty, slide);
            }
            else
            {
                DayStripShift.BeginAnimation(TranslateTransform.XProperty, null);
                DayStripShift.X = x;
            }
        }

        /// <summary>Cada "clique" da rodinha passa um dia (para baixo: dias seguintes).</summary>
        private void DayStrip_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            _wheelAcc += e.Delta;
            int steps = _wheelAcc / 120;
            if (steps == 0) return;
            _wheelAcc -= steps * 120;
            SelectDay(_selectedDate.AddDays(-steps));
        }

        private void AgendaToday_Click(object sender, RoutedEventArgs e) => SelectDay(DateTime.Today);

        private static string DayKey(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private void SelectDay(DateTime day)
        {
            var first = DateTime.Today.AddDays(-DaysBack);
            var last = DateTime.Today.AddDays(DaysAhead - 1);
            var d = day.Date < first ? first : day.Date > last ? last : day.Date;
            _selectedDate = d;
            UpdateCalendar(force: true);
        }

        /// <summary>Lembretes do dia escolhido; os sem dia (antigos) aparecem no dia de hoje.</summary>
        private bool TodoVisible(object o) =>
            o is TodoItem t && (t.Date == DayKey(_selectedDate) ||
                                (string.IsNullOrEmpty(t.Date) && _selectedDate == DateTime.Today));

        private void RefreshTodoFilter()
        {
            var view = CollectionViewSource.GetDefaultView(_todos);
            if (view.Filter == null) view.Filter = TodoVisible;
            else view.Refresh();
            UpdateTodoSummary();
        }
    }
}
