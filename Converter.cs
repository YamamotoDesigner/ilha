using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Ilha
{
    /// <summary>Erro com uma frase pronta para mostrar na ilha.</summary>
    internal sealed class ConvertException : Exception
    {
        public ConvertException(string message) : base(message) { }
    }

    /// <summary>
    /// Conversor da ilha: tudo com o que o próprio Windows já traz (sem programas extras).
    /// Imagens (PNG, JPG, PDF, ícone, comprimir, metade), PDF em imagens, áudio e vídeo
    /// (MP4, MP3, WAV, M4A) e ZIP para qualquer coisa. O resultado fica ao lado do original.
    /// </summary>
    internal static class Converter
    {
        public enum Kind { Image, Pdf, Audio, Video, Other }

        public sealed record Option(string Id, string Label, string Hint);

        private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif", ".ico" };
        private static readonly string[] AudioExt = { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".wma", ".ogg", ".opus" };
        private static readonly string[] VideoExt = { ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".m4v" };

        public static Kind KindOf(string path)
        {
            if (Directory.Exists(path)) return Kind.Other;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ImageExt.Contains(ext)) return Kind.Image;
            if (ext == ".pdf") return Kind.Pdf;
            if (AudioExt.Contains(ext)) return Kind.Audio;
            if (VideoExt.Contains(ext)) return Kind.Video;
            return Kind.Other;
        }

        /// <summary>O que dá para fazer com estes arquivos (todos do mesmo tipo; senão, só ZIP).</summary>
        public static List<Option> OptionsFor(IReadOnlyList<string> paths)
        {
            var list = new List<Option>();
            if (paths.Count == 0) return list;
            var kinds = paths.Select(KindOf).Distinct().ToList();
            bool all(params string[] exts) => paths.All(p => exts.Contains(Path.GetExtension(p).ToLowerInvariant()));
            if (kinds.Count == 1)
            {
                switch (kinds[0])
                {
                    case Kind.Image:
                        if (!all(".png")) list.Add(new("png", "PNG", L.T("Converter para PNG")));
                        if (!all(".jpg", ".jpeg", ".jfif")) list.Add(new("jpg", "JPG", L.T("Converter para JPG")));
                        list.Add(new("pdf", "PDF", paths.Count > 1 ? L.T("Juntar tudo em um PDF") : L.T("Converter para PDF")));
                        list.Add(new("compress", L.T("Comprimir"), L.T("Deixa a imagem bem mais leve")));
                        list.Add(new("half", L.T("Metade"), L.T("Reduz para metade do tamanho")));
                        if (!all(".ico")) list.Add(new("ico", L.T("Ícone"), L.T("Converter para ícone (.ico)")));
                        break;
                    case Kind.Pdf:
                        list.Add(new("pdf-png", "PNG", L.T("Cada página vira uma imagem PNG")));
                        list.Add(new("pdf-jpg", "JPG", L.T("Cada página vira uma imagem JPG")));
                        break;
                    case Kind.Video:
                        if (!all(".mp4")) list.Add(new("mp4", "MP4", L.T("Converter para MP4")));
                        list.Add(new("video-small", L.T("Comprimir"), L.T("Vídeo mais leve, em 720p")));
                        list.Add(new("mp3", L.T("Só o áudio"), L.T("Extrai o áudio em MP3")));
                        break;
                    case Kind.Audio:
                        if (!all(".mp3")) list.Add(new("mp3", "MP3", L.T("Converter para MP3")));
                        if (!all(".m4a")) list.Add(new("m4a", "M4A", L.T("Converter para M4A")));
                        if (!all(".wav")) list.Add(new("wav", "WAV", L.T("Converter para WAV")));
                        break;
                }
            }
            if (!paths.All(p => Path.GetExtension(p).Equals(".zip", StringComparison.OrdinalIgnoreCase)))
                list.Add(new("zip", "ZIP", L.T("Compactar em um arquivo ZIP")));
            return list;
        }

        /// <summary>Converte e devolve os arquivos (ou pasta) criados.</summary>
        public static async Task<List<string>> RunAsync(string id, IReadOnlyList<string> paths, IProgress<double> progress)
        {
            var results = new List<string>();
            try
            {
                if (id == "zip")
                {
                    results.Add(await Task.Run(() => Zip(paths, progress)));
                }
                else if (id == "pdf")
                {
                    results.Add(await Task.Run(() => ImagesToPdf(paths, progress)));
                }
                else
                {
                    for (int i = 0; i < paths.Count; i++)
                    {
                        string path = paths[i];
                        int index = i;
                        var part = new Progress<double>(p => progress.Report((index + Math.Clamp(p, 0, 1)) / paths.Count));
                        switch (id)
                        {
                            case "png": case "jpg": case "ico": case "compress": case "half":
                                results.Add(await Task.Run(() => ConvertImage(id, path)));
                                break;
                            case "pdf-png": case "pdf-jpg":
                                results.AddRange(await PdfToImages(path, jpg: id == "pdf-jpg", part));
                                break;
                            case "mp4": case "video-small": case "mp3": case "m4a": case "wav":
                                results.Add(await Transcode(id, path, part));
                                break;
                            default:
                                throw new ConvertException(L.T("Não sei fazer essa conversão."));
                        }
                        progress.Report((i + 1.0) / paths.Count);
                    }
                }
            }
            catch (ConvertException)
            {
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                throw new ConvertException(L.T("Sem permissão para salvar nesta pasta."));
            }
            catch (FileNotFoundException)
            {
                throw new ConvertException(L.T("O arquivo não está mais lá."));
            }
            catch (NotSupportedException)
            {
                throw new ConvertException(L.T("O Windows não conseguiu abrir este arquivo."));
            }
            catch (Exception)
            {
                throw new ConvertException(L.T("Não deu para converter este arquivo."));
            }
            return results;
        }

        // ───────────── Onde salvar ─────────────

        /// <summary>A pasta do original; se não der para gravar nela (ex.: só leitura), Downloads.</summary>
        private static string OutputFolder(string source)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(source.TrimEnd('\\', '/')));
            if (dir != null)
            {
                try
                {
                    string probe = Path.Combine(dir, ".ilha-" + Guid.NewGuid().ToString("N") + ".tmp");
                    File.WriteAllBytes(probe, Array.Empty<byte>());
                    File.Delete(probe);
                    return dir;
                }
                catch
                {
                    // Pasta protegida: cai para Downloads.
                }
            }
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloads);
            return downloads;
        }

        /// <summary>Nunca sobrescreve: "foto.png", "foto (2).png", "foto (3).png"…</summary>
        private static string UniquePath(string folder, string name, string ext)
        {
            string path = Path.Combine(folder, name + ext);
            for (int n = 2; File.Exists(path) || Directory.Exists(path); n++)
                path = Path.Combine(folder, $"{name} ({n}){ext}");
            return path;
        }

        private static string BaseName(string path) => Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));

        // ───────────── Imagens ─────────────

        /// <summary>Abre a imagem já na orientação certa (fotos de celular guardam o giro à parte).</summary>
        private static BitmapSource LoadImage(string path)
        {
            BitmapFrame frame;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                // Ícone: o maior tamanho; nos outros formatos, o primeiro quadro.
                frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            }
            BitmapSource image = frame;
            int orientation = 1;
            try
            {
                if (frame.Metadata is BitmapMetadata meta && meta.GetQuery("System.Photo.Orientation") is ushort o)
                    orientation = o;
            }
            catch
            {
                // Formato sem metadados.
            }
            double angle = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
            if (angle != 0)
                image = new TransformedBitmap(image, new RotateTransform(angle));
            image.Freeze();
            return image;
        }

        private static BitmapSource Resize(BitmapSource image, double scale)
        {
            if (scale >= 0.999) return image;
            var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            scaled.Freeze();
            return scaled;
        }

        private static BitmapSource FitWithin(BitmapSource image, int maxSide) =>
            Resize(image, Math.Min(1.0, maxSide / (double)Math.Max(image.PixelWidth, image.PixelHeight)));

        private static byte[] Pixels(BitmapSource image, out int width, out int height)
        {
            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            width = bgra.PixelWidth;
            height = bgra.PixelHeight;
            var pixels = new byte[width * height * 4];
            bgra.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }

        private static bool HasTransparency(BitmapSource image)
        {
            var f = image.Format;
            if (f == PixelFormats.Bgr24 || f == PixelFormats.Rgb24 || f == PixelFormats.Bgr32 || f == PixelFormats.Gray8 ||
                f == PixelFormats.Gray16 || f == PixelFormats.Rgb48 || f == PixelFormats.Cmyk32 || f == PixelFormats.BlackWhite)
                return false;
            var pixels = Pixels(image, out _, out _);
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] < 250) return true;
            return false;
        }

        /// <summary>Sem transparência (JPG e PDF não têm): o que era transparente vira branco.</summary>
        private static BitmapSource OnWhite(BitmapSource image)
        {
            var pixels = Pixels(image, out int w, out int h);
            var rgb = new byte[w * h * 3];
            for (int i = 0, o = 0; i < pixels.Length; i += 4, o += 3)
            {
                int a = pixels[i + 3];
                rgb[o] = (byte)((pixels[i] * a + 255 * (255 - a)) / 255);
                rgb[o + 1] = (byte)((pixels[i + 1] * a + 255 * (255 - a)) / 255);
                rgb[o + 2] = (byte)((pixels[i + 2] * a + 255 * (255 - a)) / 255);
            }
            var flat = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, rgb, w * 3);
            flat.Freeze();
            return flat;
        }

        private static byte[] Encode(BitmapEncoder encoder, BitmapSource image)
        {
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        private static byte[] Jpeg(BitmapSource image, int quality) =>
            Encode(new JpegBitmapEncoder { QualityLevel = quality }, OnWhite(image));

        private static byte[] Png(BitmapSource image) => Encode(new PngBitmapEncoder(), image);

        private static string ConvertImage(string id, string path)
        {
            var image = LoadImage(path);
            string folder = OutputFolder(path), name = BaseName(path);
            long before = new FileInfo(path).Length;
            switch (id)
            {
                case "png":
                {
                    string output = UniquePath(folder, name, ".png");
                    File.WriteAllBytes(output, Png(image));
                    return output;
                }
                case "jpg":
                {
                    string output = UniquePath(folder, name, ".jpg");
                    File.WriteAllBytes(output, Jpeg(image, 92));
                    return output;
                }
                case "half":
                {
                    bool png = HasTransparency(image) || Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase);
                    var half = Resize(image, 0.5);
                    string output = UniquePath(folder, name + L.T("-metade"), png ? ".png" : ".jpg");
                    File.WriteAllBytes(output, png ? Png(half) : Jpeg(half, 90));
                    return output;
                }
                case "compress":
                {
                    // Com transparência continua PNG (só diminui se for enorme); sem, vira JPG enxuto.
                    bool alpha = HasTransparency(image);
                    byte[] data = alpha ? Png(FitWithin(image, 2000)) : Jpeg(FitWithin(image, 2560), 74);
                    if (data.Length >= before * 0.95)
                        throw new ConvertException(L.T("Esta imagem já está bem leve."));
                    string output = UniquePath(folder, name + L.T("-leve"), alpha ? ".png" : ".jpg");
                    File.WriteAllBytes(output, data);
                    return output;
                }
                case "ico":
                {
                    string output = UniquePath(folder, name, ".ico");
                    File.WriteAllBytes(output, Icon(image));
                    return output;
                }
            }
            throw new ConvertException(L.T("Não sei fazer essa conversão."));
        }

        /// <summary>Ícone com 256, 48, 32 e 16 px, cada um centralizado num quadrado transparente.</summary>
        private static byte[] Icon(BitmapSource image)
        {
            int[] sizes = { 256, 48, 32, 16 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
            {
                double k = size / (double)Math.Max(image.PixelWidth, image.PixelHeight);
                var scaled = new TransformedBitmap(image, new ScaleTransform(k, k));
                var src = Pixels(scaled, out int w, out int h);
                w = Math.Min(w, size);
                h = Math.Min(h, size);
                var canvas = new byte[size * size * 4];
                int ox = (size - w) / 2, oy = (size - h) / 2, stride = scaled.PixelWidth * 4;
                for (int y = 0; y < h; y++)
                    System.Buffer.BlockCopy(src, y * stride, canvas, ((y + oy) * size + ox) * 4, w * 4);
                if (size >= 256)
                {
                    var square = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, canvas, size * 4);
                    images.Add(Png(square));
                    continue;
                }
                // Tamanhos pequenos no formato clássico (de baixo para cima + máscara), que todo programa lê.
                using var dib = new MemoryStream();
                using var dw = new BinaryWriter(dib);
                dw.Write(40);
                dw.Write(size);
                dw.Write(size * 2);
                dw.Write((short)1);
                dw.Write((short)32);
                dw.Write(0);
                dw.Write(size * size * 4);
                dw.Write(0);
                dw.Write(0);
                dw.Write(0);
                dw.Write(0);
                for (int y = size - 1; y >= 0; y--) dw.Write(canvas, y * size * 4, size * 4);
                dw.Write(new byte[((size + 31) / 32 * 4) * size]);
                dw.Flush();
                images.Add(dib.ToArray());
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write((short)0);
            bw.Write((short)1);
            bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((short)1);
                bw.Write((short)32);
                bw.Write(images[i].Length);
                bw.Write(offset);
                offset += images[i].Length;
            }
            foreach (var png in images) bw.Write(png);
            bw.Flush();
            return ms.ToArray();
        }

        // ───────────── Imagens → PDF ─────────────

        /// <summary>Um PDF simples: cada imagem numa página do tamanho dela (lado maior de uma folha A4).</summary>
        private static string ImagesToPdf(IReadOnlyList<string> paths, IProgress<double> progress)
        {
            string output = UniquePath(OutputFolder(paths[0]), BaseName(paths[0]), ".pdf");
            var offsets = new List<long>();
            using (var fs = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            {
                void Text(string s)
                {
                    var bytes = Encoding.ASCII.GetBytes(s);
                    fs.Write(bytes, 0, bytes.Length);
                }
                void Begin() => offsets.Add(fs.Position);
                static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

                Text("%PDF-1.4\n");
                fs.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }, 0, 6); // marca de arquivo binário
                int n = paths.Count;
                // Objetos: 1 catálogo, 2 lista de páginas e, por página, página + conteúdo + imagem.
                Begin();
                Text("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                Begin();
                Text("2 0 obj\n<< /Type /Pages /Count " + n + " /Kids [" +
                     string.Join(" ", Enumerable.Range(0, n).Select(i => (3 + i * 3) + " 0 R")) + "] >>\nendobj\n");
                for (int i = 0; i < n; i++)
                {
                    var image = FitWithin(LoadImage(paths[i]), 3600);
                    byte[] jpeg = Jpeg(image, 90);
                    double wPt = image.PixelWidth * 0.75, hPt = image.PixelHeight * 0.75;
                    double fit = Math.Min(1, 842 / Math.Max(wPt, hPt));
                    wPt *= fit;
                    hPt *= fit;
                    int page = 3 + i * 3, content = page + 1, picture = page + 2;
                    Begin();
                    Text($"{page} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(wPt)} {N(hPt)}] " +
                         $"/Resources << /XObject << /Im0 {picture} 0 R >> >> /Contents {content} 0 R >>\nendobj\n");
                    string draw = $"q {N(wPt)} 0 0 {N(hPt)} 0 0 cm /Im0 Do Q";
                    Begin();
                    Text($"{content} 0 obj\n<< /Length {draw.Length} >>\nstream\n{draw}\nendstream\nendobj\n");
                    Begin();
                    Text($"{picture} 0 obj\n<< /Type /XObject /Subtype /Image /Width {image.PixelWidth} /Height {image.PixelHeight} " +
                         $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
                    fs.Write(jpeg, 0, jpeg.Length);
                    Text("\nendstream\nendobj\n");
                    progress.Report((i + 1.0) / n);
                }
                long xref = fs.Position;
                Text("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
                foreach (long o in offsets) Text(o.ToString("0000000000") + " 00000 n \n");
                Text("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            }
            return output;
        }

        // ───────────── PDF → imagens ─────────────

        private static async Task<List<string>> PdfToImages(string path, bool jpg, IProgress<double> progress)
        {
            PdfDocument doc;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
                doc = await PdfDocument.LoadFromFileAsync(file);
            }
            catch (Exception)
            {
                throw new ConvertException(L.T("Não deu para abrir este PDF (ele tem senha?)."));
            }

            string folder = OutputFolder(path), name = BaseName(path), ext = jpg ? ".jpg" : ".png";
            int count = (int)doc.PageCount;
            // Muitas páginas: uma pasta só para elas, em vez de espalhar arquivos.
            bool ownFolder = count > 3;
            if (ownFolder)
            {
                folder = UniquePath(folder, name + L.T(" - páginas"), "");
                Directory.CreateDirectory(folder);
            }

            var outputs = new List<string>();
            for (int i = 0; i < count; i++)
            {
                using var page = doc.GetPage((uint)i);
                var options = new PdfPageRenderOptions
                {
                    // O dobro do tamanho da página: nítido para ler e imprimir.
                    DestinationWidth = (uint)Math.Clamp(page.Size.Width * 2, 200, 6000),
                };
                if (jpg) options.BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId;
                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, options);
                string output = UniquePath(folder, count == 1 ? name : $"{name}-{i + 1}", ext);
                stream.Seek(0);
                using (var input = stream.AsStream())
                using (var fs = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                    await input.CopyToAsync(fs);
                outputs.Add(output);
                progress.Report((i + 1.0) / count);
            }
            return ownFolder ? new List<string> { folder } : outputs;
        }

        // ───────────── Áudio e vídeo ─────────────

        private static async Task<string> Transcode(string id, string path, IProgress<double> progress)
        {
            var (profile, ext, suffix) = id switch
            {
                "mp4" => (MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto), ".mp4", ""),
                "video-small" => (MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p), ".mp4", L.T("-leve")),
                "mp3" => (MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High), ".mp3", ""),
                "m4a" => (MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High), ".m4a", ""),
                _ => (MediaEncodingProfile.CreateWav(AudioEncodingQuality.High), ".wav", ""),
            };
            string output = UniquePath(OutputFolder(path), BaseName(path) + suffix, ext);
            StorageFile? target = null;
            try
            {
                var source = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
                var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(output)!);
                target = await folder.CreateFileAsync(Path.GetFileName(output), CreationCollisionOption.FailIfExists);
                var transcoder = new MediaTranscoder { AlwaysReencode = false };
                var prepared = await transcoder.PrepareFileTranscodeAsync(source, target, profile);
                if (!prepared.CanTranscode)
                    throw new ConvertException(id is "mp3" or "m4a" or "wav" && KindOf(path) == Kind.Video
                        ? L.T("Este vídeo não tem áudio que o Windows consiga ler.")
                        : L.T("O Windows não tem o codec para este arquivo."));
                await prepared.TranscodeAsync().AsTask(new Progress<double>(p => progress.Report(p / 100.0)));
                return output;
            }
            catch
            {
                // Não deixa um arquivo vazio ou pela metade para trás.
                try { if (target != null) await target.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch { }
                throw;
            }
        }

        // ───────────── ZIP ─────────────

        private static string Zip(IReadOnlyList<string> paths, IProgress<double> progress)
        {
            string name = paths.Count == 1 ? BaseName(paths[0]) : L.T("Arquivos");
            string output = UniquePath(OutputFolder(paths[0]), name, ".zip");
            try
            {
                var entries = new List<(string File, string Entry)>();
                foreach (string raw in paths)
                {
                    string path = raw.TrimEnd('\\', '/');
                    if (Directory.Exists(path))
                    {
                        string root = Path.GetDirectoryName(path) ?? path;
                        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                            entries.Add((file, Path.GetRelativePath(root, file).Replace('\\', '/')));
                    }
                    else if (File.Exists(path))
                    {
                        entries.Add((path, Path.GetFileName(path)));
                    }
                }
                if (entries.Count == 0) throw new ConvertException(L.T("Não há nada para compactar."));
                using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
                for (int i = 0; i < entries.Count; i++)
                {
                    zip.CreateEntryFromFile(entries[i].File, entries[i].Entry, CompressionLevel.Optimal);
                    progress.Report((i + 1.0) / entries.Count);
                }
                return output;
            }
            catch
            {
                try { File.Delete(output); } catch { }
                throw;
            }
        }

        // ───────────── Textos de apoio ─────────────

        public static string SizeText(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
        }

        public static long SizeOf(string path)
        {
            try
            {
                if (File.Exists(path)) return new FileInfo(path).Length;
                if (Directory.Exists(path))
                    return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            }
            catch
            {
                // Arquivo sumiu ou sem acesso: tamanho desconhecido.
            }
            return 0;
        }
    }
}
