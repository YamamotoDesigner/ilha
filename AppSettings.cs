using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Ilha
{
    public sealed class AppSettings
    {
        // Posição
        public string? MonitorDevice { get; set; }
        public bool HideOnFullscreen { get; set; } = true;

        // Aparência
        /// <summary>"Glass" (vidro com desfoque) ou "Solid" (cor sólida).</summary>
        public string Material { get; set; } = "Glass";
        /// <summary>Quanto da cor da ilha cobre o vidro, de 0 (transparente) a 100 (sólido).</summary>
        public double GlassTint { get; set; } = 42;
        public string IslandColor { get; set; } = "#000000";
        public string AccentColor { get; set; } = "#7CE3A1";
        public double Scale { get; set; } = 1.0;
        public double CornerRadius { get; set; } = 22;
        public double CollapsedWidth { get; set; } = 190;
        public double CollapsedHeight { get; set; } = 34;
        public bool ShowShadow { get; set; } = true;
        public bool SpringAnimation { get; set; } = true;

        // Comportamento
        public int HoverDelayMs { get; set; } = 0;
        public int CollapseDelayMs { get; set; } = 350;
        public bool HotkeyEnabled { get; set; } = true;
        public bool Use24h { get; set; } = true;
        public bool ShowSeconds { get; set; } = false;
        public bool AutoCheckUpdates { get; set; } = true;

        // Ilha recolhida
        public bool CollapsedShowArt { get; set; } = true;
        public bool CollapsedShowEqualizer { get; set; } = true;
        public bool CollapsedShowTimer { get; set; } = true;

        // Abas e recursos
        public bool ShowMedia { get; set; } = true;
        public bool ShowShelf { get; set; } = true;
        public bool ShowNotes { get; set; } = true;
        public bool ShowTimer { get; set; } = true;
        public bool ShowClipboard { get; set; } = true;
        public int ClipboardMax { get; set; } = 25;
        public bool ShowCalendar { get; set; } = true;
        public bool ShowMirror { get; set; } = true;
        public bool ShowStats { get; set; } = true;

        // Avisos rápidos na ilha recolhida
        public bool LiveVolume { get; set; } = true;
        public bool LiveCharging { get; set; } = true;
        public bool LiveTrack { get; set; } = true;

        // Dados
        public List<string> Shelf { get; set; } = new();
        public string Notes { get; set; } = "";
        public List<TodoItem> Todos { get; set; } = new();

        /// <summary>Restaura aparência e comportamento, preservando dados (prateleira, notas, monitor).</summary>
        public void ResetPreferences()
        {
            var d = new AppSettings();
            HideOnFullscreen = d.HideOnFullscreen;
            Material = d.Material;
            GlassTint = d.GlassTint;
            IslandColor = d.IslandColor;
            AccentColor = d.AccentColor;
            Scale = d.Scale;
            CornerRadius = d.CornerRadius;
            CollapsedWidth = d.CollapsedWidth;
            CollapsedHeight = d.CollapsedHeight;
            ShowShadow = d.ShowShadow;
            SpringAnimation = d.SpringAnimation;
            HoverDelayMs = d.HoverDelayMs;
            CollapseDelayMs = d.CollapseDelayMs;
            HotkeyEnabled = d.HotkeyEnabled;
            Use24h = d.Use24h;
            ShowSeconds = d.ShowSeconds;
            CollapsedShowArt = d.CollapsedShowArt;
            CollapsedShowEqualizer = d.CollapsedShowEqualizer;
            CollapsedShowTimer = d.CollapsedShowTimer;
            ShowMedia = d.ShowMedia;
            ShowShelf = d.ShowShelf;
            ShowNotes = d.ShowNotes;
            ShowTimer = d.ShowTimer;
            ShowClipboard = d.ShowClipboard;
            ClipboardMax = d.ClipboardMax;
            ShowCalendar = d.ShowCalendar;
            ShowMirror = d.ShowMirror;
            ShowStats = d.ShowStats;
            LiveVolume = d.LiveVolume;
            LiveCharging = d.LiveCharging;
            LiveTrack = d.LiveTrack;
        }

        private static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ilha");

        private static string FilePath => Path.Combine(Folder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch
            {
                // Arquivo corrompido: começa com padrões.
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch
            {
                // Falha ao salvar não deve derrubar o app.
            }
        }
    }

    /// <summary>Inicialização com o Windows via chave Run do usuário atual.</summary>
    public static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Ilha";

        public static bool IsEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
        }

        public static void Set(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
