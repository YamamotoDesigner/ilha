using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ilha
{
    /// <summary>
    /// Ilha completa (opcional, em Configurações → Ilha): em vez de uma aba por vez, a ilha abre
    /// larga com música, agenda, tarefas, notas e timer lado a lado. Prateleira, copiados,
    /// espelho e teleprompter continuam nas suas abas.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly string[] OverviewOrder = { "home", "agenda", "tasks", "notes", "timer" };
        private const double OverviewHeight = 236, OverviewGap = 18, MaxOverviewWidth = 1500;
        private List<string> _overview = new();
        private double _overviewK = 1;

        private bool FullLayout => string.Equals(_settings.OpenLayout, "Full", StringComparison.OrdinalIgnoreCase);

        /// <summary>A aba "Tudo" (a primeira) está aberta na ilha completa.</summary>
        private bool InOverview => FullLayout && _currentTab == "home" && _overview.Count > 0;

        /// <summary>Se a página <paramref name="name"/> aparece quando a aba <paramref name="tab"/> está escolhida.</summary>
        private bool Shown(string name, string tab) =>
            FullLayout && tab == "home" && _overview.Count > 0 ? _overview.Contains(name) : name == tab;

        private double OverviewWidth(string tab) => tab switch
        {
            "home" => _settings.ShowMedia ? 350 : 150,
            "agenda" => 290,
            "tasks" => 240,
            "notes" => 250,
            "timer" => 210,
            _ => 240,
        };

        private double OverviewContentWidth() =>
            _overview.Sum(OverviewWidth) + Math.Max(0, _overview.Count - 1) * OverviewGap;

        /// <summary>Largura que cabe no monitor da ilha (pixels lógicos, antes do "Tamanho geral").</summary>
        private double AvailableIslandWidth()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            double scale = Math.Clamp(_settings.Scale, 0.6, 1.6);
            return Math.Min(MaxOverviewWidth, TargetScreen().WorkingArea.Width / dpi.DpiScaleX / scale - 48);
        }

        private void RefreshOverviewList()
        {
            var s = _settings;
            _overview = OverviewOrder.Where(t => t switch
            {
                "home" => s.ShowMedia || s.ShowStats,
                "agenda" => s.ShowCalendar,
                "tasks" => s.ShowTasks,
                "notes" => s.ShowNotes,
                "timer" => s.ShowTimer,
                _ => false,
            }).ToList();
        }

        /// <summary>Na ilha completa, cada página ganha a sua coluna; nas abas, todas ocupam o espaço inteiro.</summary>
        private void ApplyOverviewColumns()
        {
            Pages.ColumnDefinitions.Clear();
            foreach (var (_, page) in AllPages) Grid.SetColumn(page, 0);
            if (!InOverview) return;
            foreach (var name in _overview)
            {
                if (Pages.ColumnDefinitions.Count > 0)
                    Pages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(OverviewGap) });
                Pages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(OverviewWidth(name)) });
                var page = AllPages.First(p => p.Tab == name).Page;
                Grid.SetColumn(page, Pages.ColumnDefinitions.Count - 1);
            }
        }
    }
}
