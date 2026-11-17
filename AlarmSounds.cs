using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Windows.Media;

namespace Ilha
{
    /// <summary>
    /// Sons do alarme do timer. Os seis da Ilha são sintetizados na hora (nada de arquivo extra):
    /// notas suaves, com ataque macio e final que se apaga, para não assustar.
    /// A pessoa também pode escolher um arquivo de som dela (WAV, MP3, M4A…).
    /// </summary>
    public static class AlarmSounds
    {
        public sealed record Choice(string Id, string Label);

        public const string FileId = "file";

        public static IReadOnlyList<Choice> Choices => new[]
        {
            new Choice("sino", L.T("Sino")),
            new Choice("cristal", L.T("Cristal")),
            new Choice("acorde", L.T("Acorde")),
            new Choice("marimba", L.T("Marimba")),
            new Choice("pulso", L.T("Pulso")),
            new Choice("amanhecer", L.T("Amanhecer")),
            new Choice(FileId, L.T("Arquivo do computador…")),
        };

        /// <summary>Tempo entre uma repetição e a próxima (o som mais uma pausa).</summary>
        public const double RepeatSeconds = 2.6;

        private const int Rate = 44100;
        private static readonly Dictionary<string, byte[]> Cache = new();
        private static SoundPlayer? _player;
        private static MediaPlayer? _media;

        /// <summary>Toca o som escolhido nas configurações (ou o padrão, se o arquivo sumiu).</summary>
        public static void Play(AppSettings s) => Play(s.AlarmSound, s.AlarmSoundFile);

        public static void Play(string id, string? file = null)
        {
            try
            {
                if (id == FileId && !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                {
                    _media ??= new MediaPlayer();
                    _media.Stop();
                    _media.Open(new Uri(file));
                    _media.Volume = 0.9;
                    _media.Play();
                    return;
                }
                if (id == FileId) id = "sino";
                if (!Cache.TryGetValue(id, out var wav))
                {
                    wav = Wav(Synth(id));
                    Cache[id] = wav;
                }
                _player?.Stop();
                _player = new SoundPlayer(new MemoryStream(wav));
                _player.Play();
            }
            catch
            {
                SystemSounds.Exclamation.Play();
            }
        }

        public static void Stop()
        {
            try { _player?.Stop(); } catch { }
            try { _media?.Stop(); } catch { }
        }

        // ───────────── Síntese ─────────────

        private static float[] Synth(string id)
        {
            switch (id)
            {
                case "cristal":
                {
                    // Duas batidas de vidro, agudas e curtas.
                    var b = Buffer(1.5);
                    Note(b, 0.00, 1568, 1.0, 0.9, new[] { (1.0, 1.0), (2.76, 0.25), (5.4, 0.08) }, attack: 0.002);
                    Note(b, 0.18, 2093, 0.8, 0.9, new[] { (1.0, 1.0), (2.76, 0.2) }, attack: 0.002);
                    return b;
                }
                case "acorde":
                {
                    // Dó, mi e sol entrando um atrás do outro e soando juntos.
                    var b = Buffer(2.0);
                    double[] f = { 523.25, 659.25, 783.99, 1046.5 };
                    for (int i = 0; i < f.Length; i++)
                        Note(b, i * 0.11, f[i], 1.6, 0.55, new[] { (1.0, 1.0), (2.0, 0.12) }, attack: 0.03);
                    return b;
                }
                case "marimba":
                {
                    // Madeira: a fundamental e o quarto harmônico, que somem rápido.
                    var b = Buffer(1.5);
                    double[] f = { 440, 554.37, 659.25, 880 };
                    for (int i = 0; i < f.Length; i++)
                        Note(b, i * 0.14, f[i], 0.45, 0.8, new[] { (1.0, 1.0), (4.0, 0.35), (10.0, 0.05) }, attack: 0.003);
                    return b;
                }
                case "pulso":
                {
                    // Três toques curtos, duas vezes, num tom macio.
                    var b = Buffer(1.4);
                    for (int g = 0; g < 2; g++)
                        for (int i = 0; i < 3; i++)
                            Note(b, g * 0.62 + i * 0.13, 988, 0.11, 0.55, new[] { (1.0, 1.0), (2.0, 0.08) }, attack: 0.006, hold: true);
                    return b;
                }
                case "amanhecer":
                {
                    // Notas subindo (escala pentatônica), como um dia começando.
                    var b = Buffer(2.0);
                    double[] f = { 587.33, 659.25, 739.99, 880, 1174.66 };
                    for (int i = 0; i < f.Length; i++)
                        Note(b, i * 0.12, f[i], 0.9, 0.6, new[] { (1.0, 1.0), (3.0, 0.1) }, attack: 0.015);
                    return b;
                }
                default:
                {
                    // Sino: parciais de sino de verdade, com um final longo.
                    var b = Buffer(2.2);
                    Note(b, 0, 880, 2.0, 0.85, new[] { (0.5, 0.35), (1.0, 1.0), (1.19, 0.3), (2.0, 0.45), (2.76, 0.25), (5.4, 0.06) }, attack: 0.004);
                    return b;
                }
            }
        }

        private static float[] Buffer(double seconds) => new float[(int)(seconds * Rate)];

        /// <summary>Soma uma nota ao buffer: parciais (múltiplo da frequência, volume) com queda exponencial.</summary>
        private static void Note(float[] buf, double start, double freq, double decay, double gain,
                                 (double Mult, double Amp)[] partials, double attack, bool hold = false)
        {
            int s0 = (int)(start * Rate);
            int len = hold ? (int)(decay * Rate) : buf.Length - s0;
            for (int n = 0; n < len && s0 + n < buf.Length; n++)
            {
                double t = (double)n / Rate;
                double env = t < attack ? t / attack : hold ? (t > decay - 0.02 ? Math.Max(0, (decay - t) / 0.02) : 1) : Math.Exp(-(t - attack) / (decay / 4.5));
                double v = 0;
                foreach (var (mult, amp) in partials)
                {
                    // Parciais mais altos se apagam mais rápido, como num instrumento de verdade.
                    double fade = hold ? 1 : Math.Exp(-t * (mult - 1) * 1.2);
                    v += amp * fade * Math.Sin(2 * Math.PI * freq * mult * t);
                }
                buf[s0 + n] += (float)(v * env * gain);
            }
        }

        /// <summary>Normaliza, apaga o final (sem estalo) e grava em WAV 16 bits mono.</summary>
        private static byte[] Wav(float[] samples)
        {
            float peak = 0.0001f;
            foreach (var x in samples) peak = Math.Max(peak, Math.Abs(x));
            float k = 0.7f / peak;
            int tail = Math.Min(samples.Length, Rate / 20);
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int bytes = samples.Length * 2;
            w.Write("RIFF"u8.ToArray());
            w.Write(36 + bytes);
            w.Write("WAVEfmt "u8.ToArray());
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(Rate);
            w.Write(Rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8.ToArray());
            w.Write(bytes);
            for (int i = 0; i < samples.Length; i++)
            {
                float v = samples[i] * k;
                int fromEnd = samples.Length - i;
                if (fromEnd < tail) v *= (float)fromEnd / tail;
                w.Write((short)Math.Clamp((int)(v * short.MaxValue), short.MinValue, short.MaxValue));
            }
            w.Flush();
            return ms.ToArray();
        }
    }
}
