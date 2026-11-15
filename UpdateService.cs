using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ilha
{
    /// <summary>
    /// Atualização pelo GitHub Releases: consulta o último release, baixa o instalador
    /// e o executa em modo silencioso (o instalador reabre o Ilha ao terminar).
    /// </summary>
    internal sealed class UpdateService
    {
        /// <summary>Repositório "dono/nome" onde os instaladores são publicados.</summary>
        public const string Repo = "YamamotoDesigner/ilha";

        public sealed record UpdateInfo(Version Version, string DownloadUrl, long Size, string PageUrl);

        private static readonly HttpClient Http = CreateClient();

        public static Version CurrentVersion
        {
            get
            {
                var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Ilha-Updater", CurrentVersion.ToString()));
            return client;
        }

        /// <summary>Retorna a atualização disponível, ou null se já estiver na versão mais recente.</summary>
        public async Task<UpdateInfo?> CheckAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await Http.SendAsync(request, cts.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // nenhum release ainda
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
            latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
            if (latest <= CurrentVersion) return null;

            var asset = root.GetProperty("assets").EnumerateArray().FirstOrDefault(a =>
            {
                string name = a.GetProperty("name").GetString() ?? "";
                return name.StartsWith("Ilha-Setup", StringComparison.OrdinalIgnoreCase) &&
                       name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            });
            if (asset.ValueKind != JsonValueKind.Object) return null;

            return new UpdateInfo(
                latest,
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                root.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : "");
        }

        /// <summary>Baixa o instalador para uma pasta temporária e retorna o caminho.</summary>
        public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null)
        {
            string folder = Path.Combine(Path.GetTempPath(), "Ilha-update");
            if (Directory.Exists(folder))
            {
                foreach (var old in Directory.GetFiles(folder))
                {
                    try { File.Delete(old); } catch { /* em uso: ignora */ }
                }
            }
            Directory.CreateDirectory(folder);

            string target = Path.Combine(folder, $"Ilha-Setup-{info.Version}.exe");
            string partial = target + ".part";

            using (var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? info.Size;
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = File.Create(partial);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }

            // Download incompleto não pode ser executado.
            if (info.Size > 0 && new FileInfo(partial).Length != info.Size)
            {
                File.Delete(partial);
                throw new IOException("O download veio incompleto.");
            }

            File.Move(partial, target, overwrite: true);
            return target;
        }

        /// <summary>Roda o instalador sem telas; ele fecha o Ilha, atualiza e abre de novo.</summary>
        public static void LaunchInstaller(string installerPath) =>
            Process.Start(new ProcessStartInfo(installerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /AUTOUPDATE")
            {
                UseShellExecute = true,
            });
    }
}
