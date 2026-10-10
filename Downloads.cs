using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

sealed class ModelAsset {
    public string path { get; set; }
    public long size { get; set; }
    public string sha256 { get; set; }
}
static class Downloads {
    internal const string SupertonicRevision = "aafc6e32416a594460b32413efc49d7fe4ce6d46";
    internal const string WhisperRevision = "5359861c739e955e79d9a303bcbc70fb988958b1";
    internal static string Hash(string path) {
        using (var s = File.OpenRead(path)) using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
    }
    internal static bool Matches(string path, ModelAsset asset) {
        return File.Exists(path) && new FileInfo(path).Length == asset.size && Hash(path) == asset.sha256;
    }
    internal static async Task Fetch(string url, string path, ModelAsset asset, CancellationToken token, Action<long> progress) {
        token.ThrowIfCancellationRequested();
        if (Matches(path, asset)) { progress(asset.size); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string part = path + ".download-" + Guid.NewGuid().ToString("N");
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        try {
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false)) {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != asset.size)
                    throw new InvalidDataException("Размер загрузки не совпадает с манифестом: " + asset.path);
                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var destination = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true)) {
                    byte[] buffer = new byte[131072]; long done = 0; int count;
                    while ((count = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0) {
                        done += count;
                        if (done > asset.size) throw new InvalidDataException("Загрузка превышает ожидаемый размер.");
                        await destination.WriteAsync(buffer, 0, count, token).ConfigureAwait(false); progress(done);
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            if (!Matches(part, asset)) throw new InvalidDataException("Проверка SHA-256 не пройдена: " + asset.path);
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(part, path, null); else File.Move(part, path);
        } finally { if (File.Exists(part)) File.Delete(part); }
    }
    internal static List<ModelAsset> SupertonicAssets() {
        return Program.Json().Deserialize<List<ModelAsset>>(File.ReadAllText(Path.Combine(Program.Root, "model-assets.json")));
    }
    internal static ModelAsset WhisperAsset(bool turbo) {
        return new ModelAsset {
            path = turbo ? "ggml-large-v3-turbo-q5_0.bin" : "ggml-base.bin",
            size = turbo ? 574041195 : 147951465,
            sha256 = turbo ? "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2" : "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe"
        };
    }
}
