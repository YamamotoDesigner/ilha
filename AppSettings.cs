using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Ilha
{
    public sealed class AppSettings
    {
        // Primeira execução, idioma e tema
        /// <summary>Versão do desenho da ilha; ao subir, tamanhos antigos são trocados pelos novos padrões.</summary>
        public int LayoutVersion { get; set; }
        /// <summary>A tela de apresentação já foi vista.</summary>
        public bool OnboardingDone { get; set; }
        /// <summary>"pt" (Português do Brasil) ou "en" (English US); null = idioma do Windows.</summary>
        public string? Language { get; set; }
        /// <summary>"Dark" ou "Light".</summary>
        public string Theme { get; set; } = "Dark";

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
        public double CollapsedWidth { get; set; } = 160;
        public double CollapsedHeight { get; set; } = 32;
        public bool ShowShadow { get; set; } = true;
        /// <summary>A capa da música vira um brilho colorido atrás da ilha aberta.</summary>
        public bool AmbientArt { get; set; } = true;
        public bool SpringAnimation { get; set; } = true;
        /// <summary>
        /// "Attached" (colada no topo), "TopFloating" (solta, centralizada no topo, sem arrastar)
        /// ou "Floating" (solta, pode ser arrastada e redimensionada).
        /// </summary>
        public string IslandStyle { get; set; } = "Attached";
        /// <summary>Ilha flutuante presa no lugar (não arrasta).</summary>
        public bool FloatLocked { get; set; } = false;
        /// <summary>Posição da ilha flutuante: centro horizontal e topo, em pixels físicos da tela.</summary>
        public int? FloatCenterX { get; set; }
        public int? FloatTop { get; set; }
        /// <summary>Tamanho da ilha aberta (pixels lógicos, antes do "Tamanho geral"), em todos os estilos.</summary>
        public double OpenWidth { get; set; } = 560;
        public double OpenHeight { get; set; } = 220;
        /// <summary>"Side" (trilho lateral) ou "Top" (barra no topo).</summary>
        public string TabPosition { get; set; } = "Side";

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
        public bool ShowPrompter { get; set; } = true;
        /// <summary>Aviso na ilha quando um download termina (pasta Downloads).</summary>
        public bool LiveDownloads { get; set; } = true;
        /// <summary>Downloads novos vão para a prateleira.</summary>
        public bool DownloadsToShelf { get; set; } = true;
        /// <summary>Aviso quando sai versão nova.</summary>
        public bool LiveUpdate { get; set; } = true;
        /// <summary>Botão de curtir a música do Spotify.</summary>
        public bool EnableLike { get; set; } = true;

        // Avisos rápidos na ilha recolhida
        public bool LiveVolume { get; set; } = true;
        public bool LiveCharging { get; set; } = true;
        public bool LiveTrack { get; set; } = true;

        // Dados
        public List<string> Shelf { get; set; } = new();
        public string Notes { get; set; } = "";
        public List<TodoItem> Todos { get; set; } = new();
        /// <summary>Roteiros do teleprompter.</summary>
        public List<string> Scripts { get; set; } = new();
        public int ScriptIndex { get; set; }
        public double PrompterSpeed { get; set; } = 1.0;

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
            AmbientArt = d.AmbientArt;
            SpringAnimation = d.SpringAnimation;
            IslandStyle = d.IslandStyle;
            FloatLocked = d.FloatLocked;
            FloatCenterX = d.FloatCenterX;
            FloatTop = d.FloatTop;
            OpenWidth = d.OpenWidth;
            OpenHeight = d.OpenHeight;
            TabPosition = d.TabPosition;
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
            EnableLike = d.EnableLike;
            ShowPrompter = d.ShowPrompter;
            LiveDownloads = d.LiveDownloads;
            DownloadsToShelf = d.DownloadsToShelf;
            LiveUpdate = d.LiveUpdate;
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
                    return Migrate(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings());
            }
            catch
            {
                // Arquivo corrompido: começa com padrões.
            }
            return Migrate(new AppSettings());
        }

        private const int CurrentLayout = 3;

        /// <summary>
        /// Layout 3 (1.9.1): a ilha aberta ficou menor por padrão (560 × 220). Quem estava no
        /// padrão anterior (640 × 236) passa para o novo; quem escolheu outro tamanho fica como está.
        /// </summary>
        private static AppSettings Migrate(AppSettings s)
        {
            if (s.LayoutVersion < CurrentLayout)
            {
                var d = new AppSettings();
                bool wasDefault = s.LayoutVersion < 2 || (s.OpenWidth == 640 && s.OpenHeight == 236);
                if (wasDefault)
                {
                    s.OpenWidth = d.OpenWidth;
                    s.OpenHeight = d.OpenHeight;
                }
                s.LayoutVersion = CurrentLayout;
            }
            return s;
        }

        /// <summary>
        /// Instâncias de teste (desenvolvimento) leem as configurações reais mas nunca gravam:
        /// os ajustes temporários de um teste não podem vazar para o arquivo do usuário.
        /// </summary>
        public static bool ReadOnly { get; set; }

        public void Save()
        {
            if (ReadOnly) return;
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
