using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Ilha
{
    /// <summary>
    /// Saudação (primeira vez do dia que o mouse passa na ilha) e lembretes de bem-estar em horários
    /// que fazem sentido: água e esticar as pernas durante o dia, escovar os dentes depois do almoço
    /// e à noite, despertador antes de dormir e "não está tarde?" de madrugada.
    /// </summary>
    public partial class MainWindow
    {
        private bool _nudging;
        private Action? _afterNudge;
        private readonly DispatcherTimer _nudgeTimer = new();
        private const double NudgeHeight = 62;
        private readonly Random _rng = new();
        private int _nudgeCheck;

        private void InitNudges()
        {
            _nudgeTimer.Tick += (_, _) => EndNudge();
        }

        // ───── Mostrar ─────

        private void ShowNudge(string kind, string title, string text, double seconds, Action? after = null)
        {
            if (_expanded || _hiddenForFullscreen) return;
            if (_peeking) EndPeek(animate: false);
            NudgeIconHost.Child = NudgeArt.Build(kind);
            NudgeTitle.Text = title;
            NudgeText.Text = text;
            double textW = Math.Max(Measure(title, 14, FontWeights.SemiBold), Measure(text, 11.5, FontWeights.Normal));
            double width = Math.Clamp(textW + 14 + 40 + 12 + 20 + 34, 280, 580);
            _afterNudge = after;
            _nudging = true;
            AnimateSize(width, NudgeHeight, spring: _settings.SpringAnimation);
            Fade(CollapsedView, 0, 120);
            Fade(PeekView, 0, 100);
            Fade(NudgeView, 1, 260, delayMs: 140);
            if (!ReduceMotion)
            {
                var pop = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(620))
                {
                    BeginTime = TimeSpan.FromMilliseconds(120),
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 },
                };
                NudgeIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                NudgeIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
            _nudgeTimer.Stop();
            _nudgeTimer.Interval = TimeSpan.FromSeconds(seconds);
            _nudgeTimer.Start();
        }

        private void EndNudge() => EndNudge(runAfter: true);

        private void EndNudge(bool runAfter)
        {
            _nudgeTimer.Stop();
            if (!_nudging) return;
            _nudging = false;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(runAfter ? 140 : 0));
            fade.Completed += (_, _) => { if (!_nudging) NudgeIconHost.Child = null; };
            NudgeView.BeginAnimation(OpacityProperty, fade);
            var after = runAfter ? _afterNudge : null;
            _afterNudge = null;
            _collapsedKey = "";
            if (after != null) after();
            else if (!_expanded) UpdateCollapsedState(force: true);
        }

        private double Measure(string text, double size, FontWeight weight)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return ft.WidthIncludingTrailingWhitespace;
        }

        // ───── Saudação ─────

        /// <summary>Primeira vez do dia: "Bom dia, Nome!" com a carinha; depois a ilha abre.</summary>
        private bool TryGreet()
        {
            if (!_settings.Greeting || _expanded || _nudging || _hiddenForFullscreen) return false;
            string name = _settings.UserName.Trim();
            if (name.Length == 0) return false;
            string today = DayKey(DateTime.Today);
            if (_settings.LastGreeting == today) return false;
            _settings.LastGreeting = today;
            _settings.Save();
            var (title, text) = Greeting(name);
            ShowNudge("hello", title, text, 2.8, after: () =>
            {
                if (Island.IsMouseOver) Expand();
                else UpdateCollapsedState(force: true);
            });
            return true;
        }

        private (string Title, string Text) Greeting(string name)
        {
            int h = DateTime.Now.Hour;
            string first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
            string hello = h < 5 ? L.T("Boa noite") : h < 12 ? L.T("Bom dia") : h < 18 ? L.T("Boa tarde") : L.T("Boa noite");
            string[] lines = h < 12
                ? new[] { L.T("Que o seu dia seja leve."), L.T("Bora começar com calma?"), L.T("Que bom te ver por aqui.") }
                : h < 18
                    ? new[] { L.T("Como está indo o dia?"), L.T("Que bom te ver por aqui."), L.T("Uma pausa também conta.") }
                    : new[] { L.T("Hora de ir desacelerando."), L.T("Que bom te ver por aqui."), L.T("Foi um bom dia?") };
            return ($"{hello}, {first}!", lines[_rng.Next(lines.Length)]);
        }

        // ───── Lembretes de bem-estar ─────

        /// <summary>Chamado a cada segundo; confere os horários a cada 30 s.</summary>
        private void CheckNudges()
        {
            if (++_nudgeCheck % 30 != 0) return;
            if (!_settings.WellnessNudges || _expanded || _nudging || _peeking || _hiddenForFullscreen) return;
            if (IdleSeconds() > 180) return; // só com a pessoa no computador
            var now = DateTime.Now;
            // Nunca dois lembretes em menos de 25 minutos.
            if (_settings.NudgeLog.Values.Any(t => (now - t).TotalMinutes < 25)) return;

            string? pick = null;
            int h = now.Hour, m = now.Minute;
            double hm = h + m / 60.0;
            if (h >= 1 && h < 5 && SinceLast("late") >= 60) pick = "late";
            else if (hm >= 22.5 || h == 0) { if (!ShownTonight("alarm")) pick = "alarm"; else if (!ShownTonight("teeth-night")) pick = "teeth-night"; }
            else if (hm >= 13 && hm < 14.5 && !ShownToday("teeth-noon")) pick = "teeth-noon";
            else if (h >= 9 && h < 19 && SinceLast("water") >= 120) pick = "water";
            else if (hm >= 10.5 && h < 19 && SinceLast("stretch") >= 120) pick = "stretch";
            if (pick == null) return;

            _settings.NudgeLog[pick] = now;
            _settings.Save();
            ShowWellness(pick);
        }

        private void ShowWellness(string key)
        {
            string Pick(params string[] s) => s[_rng.Next(s.Length)];
            switch (key)
            {
                case "water":
                    ShowNudge("water", L.T("Já tomou água hoje?"), Pick(L.T("Um copo agora faz bem."), L.T("Seu corpo agradece."), L.T("Que tal encher a garrafa?")), 6);
                    break;
                case "stretch":
                    ShowNudge("stretch", L.T("Que tal esticar um pouco as pernas?"), Pick(L.T("Levante e dê uma volta rapidinho."), L.T("Dois minutos de pausa já ajudam."), L.T("Olhe para longe da tela um pouco.")), 6);
                    break;
                case "late":
                    ShowNudge("late", L.T("Já não está tarde?"), L.T("Será que não é hora de dormir?"), 6);
                    break;
                case "alarm":
                    ShowNudge("alarm", L.T("Colocou o relógio para despertar?"), L.T("Amanhã você agradece."), 6);
                    break;
                case "teeth-night":
                    ShowNudge("teeth", L.T("Já escovou os dentes?"), L.T("Antes de dormir, não esquece."), 6);
                    break;
                case "teeth-noon":
                    ShowNudge("teeth", L.T("Já escovou os dentes?"), L.T("Depois do almoço, uma escovada."), 6);
                    break;
            }
        }

        private double SinceLast(string key) =>
            _settings.NudgeLog.TryGetValue(key, out var t) ? (DateTime.Now - t).TotalMinutes : double.MaxValue;

        private bool ShownToday(string key) =>
            _settings.NudgeLog.TryGetValue(key, out var t) && t.Date == DateTime.Today;

        /// <summary>A "noite" vai até a 1 h: das 22 h 30 à meia-noite e pouco conta como a mesma noite.</summary>
        private bool ShownTonight(string key) =>
            _settings.NudgeLog.TryGetValue(key, out var t) && t.AddHours(-2).Date == DateTime.Now.AddHours(-2).Date;

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInput { public uint Size; public uint Time; }

        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);

        private static double IdleSeconds()
        {
            var li = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
            if (!GetLastInputInfo(ref li)) return 0;
            return unchecked((uint)Environment.TickCount - li.Time) / 1000.0;
        }
    }
}
