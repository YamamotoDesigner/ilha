using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Ilha
{
    /// <summary>
    /// Tempo esgotado: a ilha fica verde (um verde pastel, calmo), o som escolhido toca algumas vezes
    /// e tudo fica assim até a pessoa passar o mouse e dar OK.
    /// Hora cheia: de hora em hora (ou de meia em meia), a ilha se abre um instante e mostra o horário.
    /// </summary>
    public partial class MainWindow
    {
        private readonly DispatcherTimer _alarmSoundTimer = new();
        private int _alarmSoundsLeft;
        private bool _alertShowing;
        private const double AlertHeight = 62;
        private string _lastChime = "";

        private void InitAlarm()
        {
#if DEBUG
            // Testes: ILHA_TEST_ALERT=1 (tempo esgotado em 2 s), ILHA_TEST_CHIME=1 (hora cheia em 2 s).
            if (Environment.GetEnvironmentVariable("ILHA_TEST_ALERT") == "1" || Environment.GetEnvironmentVariable("ILHA_TEST_CHIME") == "1")
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    if (Environment.GetEnvironmentVariable("ILHA_TEST_ALERT") == "1") { _timerPreset = TimeSpan.FromMinutes(25); StartAlert(); }
                    else ShowChime(DateTime.Now.Date.AddHours(DateTime.Now.Hour));
                };
                t.Start();
            }
#endif
            _alarmSoundTimer.Interval = TimeSpan.FromSeconds(AlarmSounds.RepeatSeconds);
            _alarmSoundTimer.Tick += (_, _) =>
            {
                if (!_timerAlerting || _alarmSoundsLeft <= 0) { _alarmSoundTimer.Stop(); return; }
                _alarmSoundsLeft--;
                AlarmSounds.Play(_settings);
            };
        }

        // ───── Tempo esgotado ─────

        private void StartAlarmSound()
        {
            _alarmSoundsLeft = Math.Clamp(_settings.AlarmRepeats, 1, 30) - 1;
            AlarmSounds.Play(_settings);
            _alarmSoundTimer.Stop();
            if (_alarmSoundsLeft > 0) _alarmSoundTimer.Start();
        }

        private void StopAlarmSound()
        {
            _alarmSoundTimer.Stop();
            _alarmSoundsLeft = 0;
            AlarmSounds.Stop();
        }

        /// <summary>A ilha recolhe (se estava aberta) e vira o cartão verde com o OK.</summary>
        private void ShowAlertCard()
        {
            if (_expanded) Collapse();
            if (_peeking) EndPeek(animate: false);
            if (_nudging) EndNudge(runAfter: false);
            _alertShowing = true;
            int minutes = (int)Math.Round(_timerPreset.TotalMinutes);
            AlertTitle.Text = L.T("Tempo esgotado!");
            AlertText.Text = minutes > 0 ? L.F("{0} min concluídos. Bom trabalho.", minutes) : L.T("Bom trabalho.");
            AlertIconHost.Child = NudgeArt.Build("alarm");
            double textW = Math.Max(Measure(AlertTitle.Text, 14, FontWeights.SemiBold), Measure(AlertText.Text, 11.5, FontWeights.Normal));
            double width = Math.Clamp(textW + 14 + 40 + 12 + 12 + 64 + 14, 320, 560);
            AnimateSize(width, AlertHeight, spring: _settings.SpringAnimation);
            Fade(SlimFill, 0, 100);
            Fade(CollapsedView, 0, 120);
            Fade(PeekView, 0, 100);
            Fade(AlertFill, 1, 420);
            Fade(AlertView, 1, 280, delayMs: 160);
            AlertView.IsHitTestVisible = true;
            if (!ReduceMotion)
            {
                var pop = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(620))
                {
                    BeginTime = TimeSpan.FromMilliseconds(140),
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 },
                };
                AlertIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                AlertIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                PlayConnect(shake: true);
            }
        }

        private void HideAlertCard()
        {
            if (!_alertShowing) return;
            _alertShowing = false;
            AlertView.IsHitTestVisible = false;
            Fade(AlertView, 0, 140);
            Fade(AlertFill, 0, 520, delayMs: 60);
            _collapsedKey = "";
            if (!_expanded) UpdateCollapsedState(force: true);
        }

        private void AlertOk_Click(object sender, RoutedEventArgs e)
        {
            StopAlert();
            // Fica recolhida: a pessoa só queria dizer "vi".
            RestartCollapseTimer();
        }

        // ───── Chegada (fim da apresentação) ─────

        /// <summary>A primeira coisa que a ilha diz, logo depois de colar no topo da tela.</summary>
        public void ShowArrival()
        {
            var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            wait.Tick += (_, _) =>
            {
                wait.Stop();
                ShowNudge("here", L.T("Eu estou aqui agora!"), L.T("Passe o mouse em mim quando precisar."), 4.5);
            };
            wait.Start();
        }

        // ───── Hora cheia ─────

        /// <summary>Chamado a cada segundo: no minuto certo, mostra o horário uma vez.</summary>
        private void CheckChime()
        {
            if (!_settings.HourChime) return;
            var now = DateTime.Now;
            int every = _settings.HourChimeMinutes == 30 ? 30 : 60;
            if (now.Minute % every != 0 || now.Second > 20) return;
            string key = now.ToString("yyyyMMddHHmm");
            if (key == _lastChime) return;
            if (_expanded || _nudging || _peeking || _timerAlerting || _hiddenForFullscreen) return;
            _lastChime = key;
            ShowChime(now);
        }

        private void ShowChime(DateTime at)
        {
            if (_expanded || _hiddenForFullscreen || _timerAlerting) return;
            if (_peeking) EndPeek(animate: false);
            string time = at.ToString(_settings.Use24h ? "HH:mm" : "h:mm tt", L.English ? System.Globalization.CultureInfo.GetCultureInfo("en-US") : System.Globalization.CultureInfo.CurrentCulture);
            ChimeTime.Text = time;
            ChimeText.Text = at.Minute == 0
                ? (at.Hour == 12 ? L.T("Meio-dia.") : at.Hour == 0 ? L.T("Meia-noite.") : L.T("Mais uma hora se passou."))
                : L.T("Meia hora se passou.");
            ChimeIconHost.Child = NudgeArt.Build("clock");
            double width = Math.Clamp(Measure(time, 26, FontWeights.SemiBold) + Measure(ChimeText.Text, 12, FontWeights.Normal) + 14 + 40 + 12 + 12 + 34, 260, 520);
            _afterNudge = null;
            _nudging = true;
            AnimateSize(width, NudgeHeight, spring: _settings.SpringAnimation);
            Fade(SlimFill, 0, 100);
            Fade(CollapsedView, 0, 120);
            Fade(PeekView, 0, 100);
            Fade(ChimeView, 1, 300, delayMs: 160);
            if (!ReduceMotion)
            {
                // O horário entra deslizando um pouco, depois do relógio começar a girar.
                ChimeTime.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(380) });
                ChimeTimeShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(520))
                {
                    BeginTime = TimeSpan.FromMilliseconds(380),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
                ChimeText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(700) });
            }
            _nudgeTimer.Stop();
            _nudgeTimer.Interval = TimeSpan.FromSeconds(4.5);
            _nudgeTimer.Start();
        }
    }
}
