using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace Ilha
{
    /// <summary>"Espelho": prévia da webcam dentro da ilha. Só fica ligada enquanto a aba está aberta.</summary>
    internal sealed class CameraService
    {
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private MediaCapture? _capture;
        private MediaFrameReader? _reader;
        private WriteableBitmap? _bitmap;
        private byte[]? _pixels;
        private int _busy, _session;
        private Task _stopping = Task.CompletedTask;

        /// <summary>Imagem da câmera (a mesma instância é atualizada a cada quadro).</summary>
        public event Action<ImageSource>? FrameSourceChanged;

        public bool IsRunning => _reader != null;

        /// <summary>Liga a câmera. Devolve null se deu certo, ou uma mensagem para o usuário.</summary>
        public async Task<string?> StartAsync()
        {
            if (_reader != null) return null;
            int session = ++_session;
            try
            {
                // Espera a câmera anterior soltar o dispositivo antes de pedir de novo.
                try { await _stopping; } catch { }
                if (session != _session) return null;

                var groups = await MediaFrameSourceGroup.FindAllAsync();
                var choice = groups
                    .SelectMany(g => g.SourceInfos.Select(info => (Group: g, Info: info)))
                    .Where(x => x.Info.SourceKind == MediaFrameSourceKind.Color)
                    .OrderBy(x => x.Info.DeviceInformation?.EnclosureLocation?.Panel ==
                                  Windows.Devices.Enumeration.Panel.Front ? 0 : 1)
                    .ThenBy(x => x.Info.MediaStreamType == MediaStreamType.VideoPreview ? 0 : 1)
                    .FirstOrDefault();
                if (choice.Group == null) return "Nenhuma câmera encontrada.";

                var capture = new MediaCapture();
                bool exclusive = true;
                try
                {
                    await capture.InitializeAsync(Settings(choice.Group, MediaCaptureSharingMode.ExclusiveControl));
                }
                catch (Exception) when (session == _session)
                {
                    // Outro app está usando a câmera: tenta só observar.
                    capture.Dispose();
                    capture = new MediaCapture();
                    exclusive = false;
                    await capture.InitializeAsync(Settings(choice.Group, MediaCaptureSharingMode.SharedReadOnly));
                }

                var source = capture.FrameSources[choice.Info.Id];
                var format = source.SupportedFormats
                    .Where(f => f.VideoFormat.Width >= 480)
                    .OrderBy(f => Math.Abs((int)f.VideoFormat.Width - 960))
                    .ThenByDescending(f => f.FrameRate.Denominator == 0 ? 0 : f.FrameRate.Numerator / (double)f.FrameRate.Denominator)
                    .FirstOrDefault();
                if (format != null && exclusive)
                {
                    try { await source.SetFormatAsync(format); } catch { /* fica no formato atual */ }
                }

                var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
                reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
                reader.FrameArrived += OnFrameArrived;
                var status = await reader.StartAsync();
                if (status != MediaFrameReaderStartStatus.Success || session != _session)
                {
                    reader.Dispose();
                    capture.Dispose();
                    return session != _session ? null : "Não foi possível ligar a câmera.";
                }

                _capture = capture;
                _reader = reader;
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return "Sem permissão: ative \"Permitir que aplicativos da área de trabalho acessem a câmera\" em Configurações → Privacidade → Câmera.";
            }
            catch (Exception)
            {
                return "Não foi possível ligar a câmera.";
            }
        }

        private static MediaCaptureInitializationSettings Settings(MediaFrameSourceGroup group, MediaCaptureSharingMode mode) => new()
        {
            SourceGroup = group,
            SharingMode = mode,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        };

        public void Stop()
        {
            _session++;
            var reader = _reader;
            var capture = _capture;
            _reader = null;
            _capture = null;
            // A tela solta a imagem ao desligar; na próxima vez é preciso entregar uma nova.
            _bitmap = null;
            if (reader != null)
            {
                reader.FrameArrived -= OnFrameArrived;
                _stopping = reader.StopAsync().AsTask().ContinueWith(_ =>
                {
                    reader.Dispose();
                    capture?.Dispose();
                }, TaskScheduler.Default);
            }
            else
            {
                capture?.Dispose();
            }
        }

        private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return; // a tela ainda está desenhando o quadro anterior
            bool handedOff = false;
            try
            {
                using var frame = sender.TryAcquireLatestFrame();
                var source = frame?.VideoMediaFrame?.SoftwareBitmap;
                if (source == null) return;

                using var bgra = source.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                    ? SoftwareBitmap.Copy(source)
                    : SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                var pixels = _pixels;
                if (pixels == null || pixels.Length != w * h * 4) pixels = _pixels = new byte[w * h * 4];
                bgra.CopyToBuffer(pixels.AsBuffer());

                handedOff = true;
                _dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (_reader == null) return;
                        if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
                        {
                            _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                            FrameSourceChanged?.Invoke(_bitmap);
                        }
                        _bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _busy, 0);
                    }
                }, DispatcherPriority.Render);
            }
            catch
            {
                // Quadro perdido: segue para o próximo.
            }
            finally
            {
                if (!handedOff) Interlocked.Exchange(ref _busy, 0);
            }
        }
    }
}
