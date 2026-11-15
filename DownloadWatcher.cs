using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Ilha
{
    /// <summary>
    /// Observa a pasta Downloads e avisa quando um arquivo novo termina de baixar (ignora os
    /// arquivos temporários dos navegadores e espera o tamanho parar de mudar).
    /// </summary>
    internal sealed class DownloadWatcher : IDisposable
    {
        private static readonly HashSet<string> TempExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload", ".!ut", ".aria2",
        };

        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);
        private FileSystemWatcher? _watcher;

        /// <summary>Caminho completo do arquivo baixado e tamanho em bytes (na thread da interface).</summary>
        public event Action<string, long>? Completed;

        public static string? DownloadsFolder
        {
            get
            {
                try
                {
                    var id = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
                    if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var ptr) == 0)
                    {
                        string path = Marshal.PtrToStringUni(ptr)!;
                        Marshal.FreeCoTaskMem(ptr);
                        return path;
                    }
                }
                catch
                {
                    // Cai no caminho padrão abaixo.
                }
                string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                return Directory.Exists(fallback) ? fallback : null;
            }
        }

        public bool Enabled
        {
            get => _watcher != null;
            set
            {
                if (value == Enabled) return;
                if (!value)
                {
                    _watcher?.Dispose();
                    _watcher = null;
                    return;
                }
                if (DownloadsFolder is not string folder || !Directory.Exists(folder)) return;
                _watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
                };
                _watcher.Created += (_, e) => Consider(e.FullPath);
                _watcher.Renamed += (_, e) => Consider(e.FullPath); // navegadores renomeiam o .crdownload no fim
                _watcher.EnableRaisingEvents = true;
            }
        }

        private void Consider(string path)
        {
            if (TempExtensions.Contains(Path.GetExtension(path)) || Path.GetFileName(path).StartsWith('.')) return;
            lock (_pending)
            {
                if (!_pending.Add(path)) return;
            }
            _ = WaitUntilStableAsync(path);
        }

        /// <summary>Espera o arquivo parar de crescer e poder ser aberto.</summary>
        private async Task WaitUntilStableAsync(string path)
        {
            try
            {
                long last = -1;
                for (int i = 0; i < 120; i++) // até ~1 min
                {
                    await Task.Delay(500);
                    if (!File.Exists(path)) return;
                    long size = new FileInfo(path).Length;
                    if (size > 0 && size == last && CanOpen(path))
                    {
                        _dispatcher.BeginInvoke(() => Raise(path, size));
                        return;
                    }
                    last = size;
                }
            }
            catch
            {
                // Arquivo sumiu ou sem acesso: ignora.
            }
            finally
            {
                lock (_pending) _pending.Remove(path);
            }
        }

        private void Raise(string path, long size)
        {
            // O mesmo arquivo pode gerar vários eventos (criado + renomeado): avisa uma vez só.
            if (_recent.TryGetValue(path, out var when) && DateTime.Now - when < TimeSpan.FromSeconds(20)) return;
            _recent[path] = DateTime.Now;
            Completed?.Invoke(path, size);
        }

        private static bool CanOpen(string path)
        {
            try
            {
                using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string FormatSize(long bytes)
        {
            double v = bytes;
            string[] units = { "B", "KB", "MB", "GB" };
            int u = 0;
            while (v >= 1024 && u < units.Length - 1)
            {
                v /= 1024;
                u++;
            }
            return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
        }

        public void Dispose() => Enabled = false;

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
    }
}
