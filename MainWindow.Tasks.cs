using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Ilha
{
    /// <summary>
    /// Tarefas do dia: uma lista com check. As concluídas saem no dia seguinte;
    /// as que ficaram pendentes continuam na lista.
    /// </summary>
    public partial class MainWindow
    {
        private readonly ObservableCollection<TodoItem> _tasks = new();

        private void InitTasks()
        {
            string today = DayKey(DateTime.Today);
            foreach (var t in _settings.DailyTasks)
                if (!(t.Done && t.Date != today)) _tasks.Add(t);
            TaskItems.ItemsSource = _tasks;
            _tasks.CollectionChanged += (_, _) => SaveTasks();
            ((FrameworkElement)TasksProgress.Parent).SizeChanged += (_, _) => UpdateTasksSummary(animate: false);
            UpdateTasksSummary(animate: false);
        }

        /// <summary>Virou o dia: tira as tarefas concluídas nos dias anteriores.</summary>
        private void PurgeOldTasks()
        {
            string today = DayKey(DateTime.Today);
            foreach (var t in _tasks.Where(t => t.Done && t.Date != today).ToList()) _tasks.Remove(t);
        }

        private void SaveTasks()
        {
            _settings.DailyTasks = _tasks.ToList();
            _settings.Save();
            UpdateTasksSummary(animate: true);
        }

        private void UpdateTasksSummary(bool animate)
        {
            int total = _tasks.Count, done = _tasks.Count(t => t.Done);
            TasksCount.Text = total == 0 ? "" : done == total ? L.T("TUDO FEITO") : L.F("{0} DE {1}", done, total);
            TasksEmpty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
            double full = ((FrameworkElement)TasksProgress.Parent).ActualWidth;
            double to = total == 0 ? 0 : full * done / total;
            if (animate && !ReduceMotion)
                TasksProgress.BeginAnimation(WidthProperty,
                    new DoubleAnimation(to, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else
            {
                TasksProgress.BeginAnimation(WidthProperty, null);
                TasksProgress.Width = to;
            }
        }

        private void AddTask()
        {
            string text = TaskInput.Text.Trim();
            if (text.Length == 0) return;
            _tasks.Add(new TodoItem { Text = text, Date = DayKey(DateTime.Today) });
            TaskInput.Clear();
        }

        private void TaskAdd_Click(object sender, RoutedEventArgs e) => AddTask();

        private void TaskInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AddTask();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ReleaseKeyboard();
                RestartCollapseTimer();
            }
        }

        private void TaskInput_TextChanged(object sender, TextChangedEventArgs e) =>
            TaskPlaceholder.Visibility = TaskInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>O binding já mudou "Done"; marca o dia em que foi feita e salva.</summary>
        private void TaskCheck_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TodoItem item) item.Date = DayKey(DateTime.Today);
            SaveTasks();
        }

        private void TaskRemove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TodoItem item) _tasks.Remove(item);
        }
    }
}
