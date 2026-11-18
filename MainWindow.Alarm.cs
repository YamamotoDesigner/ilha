using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Ilha
{
    /// <summary>
    /// Tempo esgotado: a ilha dá uma chacoalhada discreta a cada toque do som escolhido
    /// e o alarme encerra sozinho no último toque (ou quando a pessoa toca na ilha).
    /// Hora cheia: de hora em hora (ou de meia em meia), a ilha se abre um instante e mostra o horário.
    /// </summary>
    public partial class MainWindow
    {
        private readonly DispatcherTimer _alarmSoundTimer = new();
        private int _alarmSoundsLeft;
        private bool _alertShowing;
        private string _lastChime = "";

        private void InitAlarm()
        {
#if DEBUG
            // Vitrine: ILHA_TEST_SHOWCASE=1 mostra a hora cheia (6 s) e depois o tempo esgotado (15 s).
            if (Environment.GetEnvironmentVariable("ILHA_TEST_SHOWCASE") == "1")
            {
                var a = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                a.Tick += (_, _) => { a.Stop(); ShowChime(DateTime.Now.Date.AddHours(DateTime.Now.Hour)); };
                a.Start();
                var b = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                b.Tick += (_, _) => { b.Stop(); _timerPreset = TimeSpan.FromMinutes(25); StartAlert(); };
                b.Start();
            }
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
                if (!_timerAlerting) { _alarmSoundTimer.Stop(); return; }
                if (_alarmSoundsLeft <= 0) { StopAlert(); return; } // último toque acabou: o alarme encerra sozinho
                _alarmSoundsLeft--;
                AlarmSounds.Play(_settings);
                ShakeIsland(gentle: true);
            };
        }

        // ───── Tempo esgotado ─────

        private void StartAlarmSound()
        {
            _alarmSoundsLeft = Math.Clamp(_settings.AlarmRepeats, 1, 30) - 1;
            AlarmSounds.Play(_settings);
            _alarmSoundTimer.Stop();
            _alarmSoundTimer.Start();
        }

        private void StopAlarmSound()
        {
            _alarmSoundTimer.Stop();
            _alarmSoundsLeft = 0;
            AlarmSounds.Stop();
        }

        /// <summary>Tempo esgotado: a ilha só dá uma chacoalhada discreta, sem mudar de cor.</summary>
        private void ShowAlertCard()
        {
            if (_peeking) EndPeek(animate: false);
            if (_nudging) EndNudge(runAfter: false);
            ShakeIsland(gentle: true);
        }

        private void HideAlertCard()
        {
            _alertShowing = false;
        }

        /// <summary>Clicar na ilha enquanto o alarme toca: para o som.</summary>
        private bool TryStopAlertByClick()
        {
            if (!_timerAlerting) return false;
            StopAlert();
            RestartCollapseTimer();
            return true;
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
            string time = at.ToString(_settings.Use24h ? "HH:mm" : "h:mm tt", L.English ? System.Globalization.CultureInfo.GetCultureInfo("en-US") : System.Globalization.CultureInfo.CurrentCulture);
            ShowMinimal(time, 17, 2.2);
        }

        /// <summary>
        /// Só um texto, sem ícone: a ilha cresce um pouco, o texto sobe de leve e some logo.
        /// Usado na hora cheia e no "Eu estou aqui agora!".
        /// </summary>
        private void ShowMinimal(string text, double fontSize, double seconds)
        {
            if (_expanded || _hiddenForFullscreen || _timerAlerting) return;
            if (_peeking) EndPeek(animate: false);
            ChimeTime.Text = text;
            ChimeTime.FontSize = fontSize;
            double height = Math.Max(_settings.CollapsedHeight + 8, 40);
            double width = Math.Max(_settings.CollapsedWidth, Measure(text, fontSize, FontWeights.SemiBold) + 56);
            ChimeView.Height = height;
            _afterNudge = null;
            _nudging = true;
            AnimateSize(width, height, spring: _settings.SpringAnimation);
            Fade(SlimFill, 0, 100);
            Fade(CollapsedView, 0, 120);
            Fade(PeekView, 0, 100);
            Fade(ChimeView, 1, 260, delayMs: 140);
            if (!ReduceMotion)
            {
                ChimeTime.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = TimeSpan.FromMilliseconds(180) });
                ChimeTimeShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(460))
                {
                    BeginTime = TimeSpan.FromMilliseconds(180),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            }
            _nudgeTimer.Stop();
            _nudgeTimer.Interval = TimeSpan.FromSeconds(seconds);
            _nudgeTimer.Start();
        }
    }
}
