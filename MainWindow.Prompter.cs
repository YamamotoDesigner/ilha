using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// Teleprompter: o roteiro rola logo abaixo da câmera, com contagem 3-2-1, linha atual em
    /// destaque, velocidade ajustável e marcações como [pausa] mostradas como dicas.
    /// </summary>
    public partial class MainWindow
    {
        private const double PrompterBasePixelsPerSecond = 30;
        private static readonly Regex PrompterCue = new(@"\[([^\]]+)\]", RegexOptions.Compiled);

        private bool _prompterRunning, _prompterLocked, _prompterEditing, _prompterCounting;
        private double _prompterOffset;
        private TimeSpan _prompterLastFrame;
        private DispatcherTimer? _prompterCountdown;

        private string SampleScript => L.T(
            "Olá! Este é o teleprompter do Ilha.\n\nO texto passa logo abaixo da câmera, então você lê olhando para a lente. [sorria]\n\n" +
            "A linha atual fica em destaque, e as que você já leu ficam mais apagadas. [pausa]\n\n" +
            "Toque em Editar para escrever o seu roteiro. Use colchetes para deixar lembretes, como [respire].");

        private void LoadPrompter()
        {
            var s = _settings;
            if (s.Scripts.Count == 0) s.Scripts.Add(SampleScript);
            s.ScriptIndex = Math.Clamp(s.ScriptIndex, 0, s.Scripts.Count - 1);
            s.PrompterSpeed = Math.Clamp(s.PrompterSpeed, 0.5, 3);
            PrompterStop(resetToStart: true);
            BuildPrompterLines();
            UpdatePrompterChrome();
        }

        private string CurrentScript => _settings.Scripts[_settings.ScriptIndex];

        private void UpdatePrompterChrome()
        {
            var s = _settings;
            string first = CurrentScript.Split('\n').Select(l => PrompterCue.Replace(l, "").Trim())
                .FirstOrDefault(l => l.Length > 0) ?? "";
            PrompterTitle.Text = first.Length == 0 ? L.F("Roteiro {0}", s.ScriptIndex + 1) : first;
            PrompterPageText.Text = $"{s.ScriptIndex + 1}/{s.Scripts.Count}";
            PrompterSpeedText.Text = $"{s.PrompterSpeed:0.0}×";
            PrompterEditText.Text = _prompterEditing ? L.T("Concluir") : L.T("Editar");
            PrompterLockGlyph.Filled = _prompterLocked;
            PrompterLock.Foreground = _prompterLocked ? (Brush)Resources["Accent"] : Ink(0xA6);
            PrompterStartGlyph.Data = _prompterRunning ? GlyphPause : GlyphPlay;
            PrompterStartText.Text = _prompterRunning ? L.T("Pausar") : L.T("Iniciar");
            UpdatePrompterRemaining();
        }

        /// <summary>Um parágrafo por bloco; [marcações] viram dicas coloridas, não texto lido.</summary>
        private void BuildPrompterLines()
        {
            PrompterLines.Children.Clear();
            var paragraphs = CurrentScript.Replace("\r", "").Split('\n')
                .Select(p => p.Trim()).Where(p => p.Length > 0);
            foreach (var paragraph in paragraphs)
            {
                var block = new TextBlock
                {
                    FontSize = 18,
                    FontWeight = FontWeights.Medium,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10),
                    LineHeight = 25,
                };
                int last = 0;
                foreach (Match m in PrompterCue.Matches(paragraph))
                {
                    if (m.Index > last) block.Inlines.Add(new Run(paragraph[last..m.Index]));
                    var cue = new Run($" {m.Groups[1].Value} ") { FontSize = 12.5, FontStyle = FontStyles.Italic, FontWeight = FontWeights.SemiBold };
                    cue.SetResourceReference(TextElement.ForegroundProperty, "Accent");
                    block.Inlines.Add(cue);
                    last = m.Index + m.Length;
                }
                if (last < paragraph.Length) block.Inlines.Add(new Run(paragraph[last..]));
                PrompterLines.Children.Add(block);
            }
            SizePrompterLines();
            PrompterIntro.Visibility = _prompterOffset <= 0 && !_prompterRunning && !_prompterEditing ? Visibility.Visible : Visibility.Collapsed;
            PaintPrompterLines();
        }

        private void SizePrompterLines()
        {
            if (PrompterViewport.ActualWidth > 0) PrompterLines.Width = Math.Max(40, PrompterViewport.ActualWidth - 32);
            PositionPrompter();
        }

        /// <summary>A linha lida fica a cerca de 1/3 da altura, logo abaixo da câmera.</summary>
        private double PrompterReadLine => Math.Max(10, PrompterViewport.ActualHeight * 0.3);

        private double PrompterTotalHeight
        {
            get
            {
                PrompterLines.UpdateLayout();
                return PrompterLines.ActualHeight;
            }
        }

        private void PositionPrompter()
        {
            PrompterShift.Y = PrompterReadLine - _prompterOffset;
            // Antes de começar o texto fica escondido atrás da mensagem de início.
            PrompterLines.Opacity = PrompterIntro.Visibility == Visibility.Visible ? 0.12 : _prompterCounting ? 0.18 : 1;
        }

        /// <summary>Lida = apagada, atual = forte, próximas = médias.</summary>
        private void PaintPrompterLines()
        {
            double y = 0;
            foreach (var block in PrompterLines.Children.OfType<TextBlock>())
            {
                double h = block.ActualHeight + block.Margin.Bottom;
                bool read = y + h <= _prompterOffset;
                bool current = !read && y <= _prompterOffset + 1;
                block.Foreground = current ? Ink(0xFF) : read ? Ink(0x4D) : Ink(0x99);
                y += h;
            }
        }

        private void UpdatePrompterRemaining()
        {
            if (PrompterLines.ActualHeight <= 0) { PrompterLeft.Text = ""; return; }
            double left = Math.Max(0, PrompterLines.ActualHeight - _prompterOffset);
            var t = TimeSpan.FromSeconds(Math.Ceiling(left / (PrompterBasePixelsPerSecond * _settings.PrompterSpeed)));
            PrompterLeft.Text = L.F("{0} restantes", t.ToString(@"m\:ss"));
        }

        // ───────────── Iniciar, pausar, contagem ─────────────

        private void PrompterStart_Click(object sender, RoutedEventArgs e)
        {
            if (_prompterEditing) PrompterEdit_Click(sender, e);
            if (_prompterRunning || _prompterCounting)
            {
                PrompterStop(resetToStart: false);
                return;
            }
            if (_prompterOffset >= PrompterTotalHeight - 1) _prompterOffset = 0; // terminou: recomeça
            if (_prompterOffset <= 0) StartPrompterCountdown();
            else BeginPrompterScroll();
        }

        private void StartPrompterCountdown()
        {
            _prompterCounting = true;
            PrompterIntro.Visibility = Visibility.Collapsed;
            PositionPrompter();
            PrompterStartGlyph.Data = GlyphPause;
            PrompterStartText.Text = L.T("Pausar");
            int n = 3;
            ShowCount(n);
            _prompterCountdown?.Stop();
            _prompterCountdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _prompterCountdown.Tick += (_, _) =>
            {
                if (--n > 0)
                {
                    ShowCount(n);
                    return;
                }
                _prompterCountdown!.Stop();
                _prompterCounting = false;
                PrompterCount.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
                BeginPrompterScroll();
            };
            _prompterCountdown.Start();

            void ShowCount(int value)
            {
                PrompterCount.Text = value.ToString();
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                PrompterCountScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.5, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
                PrompterCountScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.5, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
                PrompterCount.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
                {
                    KeyFrames =
                    {
                        new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))),
                        new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))),
                        new LinearDoubleKeyFrame(0.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(980))),
                    },
                });
            }
        }

        private void BeginPrompterScroll()
        {
            _prompterRunning = true;
            _prompterLastFrame = TimeSpan.Zero;
            PrompterIntro.Visibility = Visibility.Collapsed;
            CompositionTarget.Rendering -= OnPrompterFrame;
            CompositionTarget.Rendering += OnPrompterFrame;
            UpdatePrompterChrome();
            PositionPrompter();
        }

        /// <summary>Rolagem suave, quadro a quadro, na velocidade escolhida.</summary>
        private void OnPrompterFrame(object? sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (_prompterLastFrame == TimeSpan.Zero || now == _prompterLastFrame)
            {
                _prompterLastFrame = now;
                return;
            }
            double dt = Math.Min(0.1, (now - _prompterLastFrame).TotalSeconds);
            _prompterLastFrame = now;
            _prompterOffset += PrompterBasePixelsPerSecond * _settings.PrompterSpeed * dt;
            double total = PrompterLines.ActualHeight;
            if (_prompterOffset >= total)
            {
                _prompterOffset = total;
                PrompterStop(resetToStart: false);
            }
            PositionPrompter();
            PaintPrompterLines();
            UpdatePrompterRemaining();
        }

        private void PrompterStop(bool resetToStart)
        {
            _prompterRunning = false;
            _prompterCounting = false;
            _prompterCountdown?.Stop();
            PrompterCount.BeginAnimation(OpacityProperty, null);
            PrompterCount.Opacity = 0;
            CompositionTarget.Rendering -= OnPrompterFrame;
            if (resetToStart) _prompterOffset = 0;
            PrompterIntro.Visibility = _prompterOffset <= 0 && !_prompterEditing ? Visibility.Visible : Visibility.Collapsed;
            PositionPrompter();
            PaintPrompterLines();
            UpdatePrompterChrome();
        }

        private void PrompterRestart_Click(object sender, RoutedEventArgs e)
        {
            bool wasRunning = _prompterRunning;
            PrompterStop(resetToStart: true);
            if (wasRunning) StartPrompterCountdown();
        }

        private void PrompterSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag } || !double.TryParse(tag, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double delta)) return;
            _settings.PrompterSpeed = Math.Round(Math.Clamp(_settings.PrompterSpeed + delta, 0.5, 3), 1);
            _settings.Save();
            UpdatePrompterChrome();
        }

        private void PrompterLock_Click(object sender, RoutedEventArgs e)
        {
            _prompterLocked = !_prompterLocked;
            UpdatePrompterChrome();
        }

        /// <summary>Rodinha do mouse ajusta a posição (desligado com o cadeado).</summary>
        private void PrompterViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_prompterLocked || _prompterEditing) return;
            e.Handled = true;
            _prompterOffset = Math.Clamp(_prompterOffset - e.Delta * 0.25, 0, Math.Max(0, PrompterTotalHeight - 1));
            if (_prompterOffset > 0) PrompterIntro.Visibility = Visibility.Collapsed;
            PositionPrompter();
            PaintPrompterLines();
            UpdatePrompterRemaining();
        }

        // ───────────── Roteiros ─────────────

        private void PrompterEdit_Click(object sender, RoutedEventArgs e)
        {
            _prompterEditing = !_prompterEditing;
            if (_prompterEditing)
            {
                PrompterStop(resetToStart: true);
                PrompterBox.Text = CurrentScript;
                PrompterBox.Visibility = Visibility.Visible;
                PrompterViewport.Visibility = Visibility.Collapsed;
                PrompterIntro.Visibility = Visibility.Collapsed;
                PrompterBox.Focus();
                PrompterBox.CaretIndex = PrompterBox.Text.Length;
            }
            else
            {
                PrompterBox.Visibility = Visibility.Collapsed;
                PrompterViewport.Visibility = Visibility.Visible;
                ReleaseKeyboard();
                BuildPrompterLines();
            }
            UpdatePrompterChrome();
        }

        private void PrompterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_prompterEditing) return;
            _settings.Scripts[_settings.ScriptIndex] = PrompterBox.Text;
            _notesSave.Stop();
            _notesSave.Start(); // salva junto com as notas, com o mesmo atraso
            UpdatePrompterChrome();
        }

        /// <summary>Anterior/próximo roteiro; passar do último cria um novo, já em edição.</summary>
        private void PrompterPage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int delta)) return;
            var s = _settings;
            bool wasEditing = _prompterEditing;
            if (_prompterEditing) PrompterEdit_Click(sender, e);

            // Roteiros vazios somem ao sair deles (sempre fica pelo menos um).
            if (s.Scripts.Count > 1 && string.IsNullOrWhiteSpace(CurrentScript))
            {
                s.Scripts.RemoveAt(s.ScriptIndex);
                if (delta > 0) delta = 0;
            }
            int next = s.ScriptIndex + delta;
            bool create = next >= s.Scripts.Count && delta > 0 && !string.IsNullOrWhiteSpace(s.Scripts[^1]);
            if (create) s.Scripts.Add("");
            s.ScriptIndex = Math.Clamp(next, 0, s.Scripts.Count - 1);
            s.Save();
            LoadPrompter();
            if (create || (wasEditing && string.IsNullOrWhiteSpace(CurrentScript))) PrompterEdit_Click(sender, e);
        }
    }
}
