using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed partial class BuilderForm {
    readonly Button checkUpdate = new GlowButton(), installUpdate = new GlowButton();
    readonly CheckBox autoUpdateCheck = new GrayCheck();
    readonly Label updateState = new Label();
    ReleasePackage availableUpdate;

    void BuildUpdates(TabSheet page) {
        var rows = Rows(page, new[] { 42, 42, 56, 48, 44, 90, 80 });
        rows.Controls.Add(new Label { Text = "Обновления программы", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        rows.Controls.Add(new Label { Text = "Установлена версия " + UpdateService.InstalledVersion, AutoSize = true, Tag = "muted" });
        var link = new LinkLabel { Text = "Voltur792 / supertonic-voice-builder · GitHub Releases", AutoSize = true, LinkColor = Color.FromArgb(75, 224, 199) };
        link.LinkClicked += delegate { Process.Start(UpdateService.Repository + "/releases"); }; rows.Controls.Add(link);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        checkUpdate.Text = "Проверить обновления"; checkUpdate.Size = new Size(225, 36);
        installUpdate.Text = "Скачать и обновить"; installUpdate.Size = new Size(225, 36); installUpdate.Enabled = false;
        buttons.Controls.AddRange(new Control[] { checkUpdate, installUpdate }); rows.Controls.Add(buttons);
        autoUpdateCheck.Text = "Проверять новую версию при запуске"; autoUpdateCheck.AutoSize = true; rows.Controls.Add(autoUpdateCheck);
        updateState.Text = "Нажми «Проверить обновления». Установка станет доступна, когда появится новый публичный релиз.";
        updateState.Dock = DockStyle.Fill; rows.Controls.Add(updateState);
        rows.Controls.Add(new Label { Text = "Перед установкой проверяются размер и SHA-256 архива. Программа закроется и откроется снова.\nЗагруженные модели, созданные голоса и настройки сохраняются.\nПроверка при запуске — по желанию; установка начинается только по нажатию кнопки.", Dock = DockStyle.Fill, Tag = "muted" });
        inputs.AddRange(new Control[] { checkUpdate, installUpdate, autoUpdateCheck });
        checkUpdate.Click += async delegate { await CheckUpdates(); };
        installUpdate.Click += async delegate { await InstallUpdate(); };
        string preference = Path.Combine(Program.Root, "update-check.txt");
        try { autoUpdateCheck.Checked = File.Exists(preference) && File.ReadAllText(preference).Trim() == "yes"; } catch (IOException) {}
        autoUpdateCheck.CheckedChanged += delegate { try { File.WriteAllText(preference, autoUpdateCheck.Checked ? "yes" : "no"); } catch (IOException ex) { Append(ex.Message); } catch (UnauthorizedAccessException ex) { Append(ex.Message); } };
        Shown += async delegate { if (autoUpdateCheck.Checked) await CheckUpdates(); };
        string error = Path.Combine(Program.Root, "last-update-error.txt");
        if (File.Exists(error)) { try { updateState.Text = "Предыдущее обновление не установлено: " + File.ReadAllText(error); } catch (IOException) {} }
    }
    async Task CheckUpdates() {
        if (busy) return;
        availableUpdate = null; BeginJob("Проверяю GitHub Releases…"); budget = null;
        updateState.Text = "Подключение к GitHub…";
        try {
            var release = await UpdateService.Check(cancellation.Token);
            if (release == null) updateState.Text = "Релиз обновления недоступен. Попробуй повторить проверку позже.";
            else if (release.Version <= UpdateService.InstalledVersion) updateState.Text = "Установлена актуальная версия. Последний релиз: " + release.Version + ".";
            else {
                availableUpdate = release;
                updateState.Text = "Доступна версия " + release.Version + " · " + (release.Asset.size / 1048576.0).ToString("F1", CultureInfo.CurrentCulture) + " МБ.\nНажми «Скачать и обновить», чтобы установить её.";
            }
            state.Text = "Проверка обновлений завершена.";
        } catch (OperationCanceledException) { updateState.Text = cancelled ? "Проверка остановлена." : "GitHub не ответил вовремя. Попробуй ещё раз."; state.Text = updateState.Text; }
        catch (Exception ex) { updateState.Text = "Не удалось проверить обновления: " + ex.Message; state.Text = "Проверка не завершена."; Append(updateState.Text); }
        finally { EndJob(); }
    }
    async Task InstallUpdate() {
        if (busy || availableUpdate == null) return;
        var release = availableUpdate;
        if (MessageBox.Show(this, "Установить версию " + release.Version + "? После загрузки программа перезапустится. Модели, голоса и настройки сохранятся.", "Обновление", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        BeginJob("Скачиваю обновление…"); budget = null;
        try {
            string session = Path.Combine(Program.Root, "updates", Guid.NewGuid().ToString("N"));
            UpdateService.SafePath(Program.Root, session); Directory.CreateDirectory(session);
            string zip = Path.Combine(session, release.Asset.path);
            var report = new Progress<long>(done => { progress.Value = Math.Min(99, (int)(100 * done / release.Asset.size)); updateState.Text = "Загрузка обновления · " + progress.Value + "%"; });
            await Task.Run(async delegate {
                await Downloads.Fetch(release.Url, zip, release.Asset, cancellation.Token, done => ((IProgress<long>)report).Report(done));
                UpdateService.Stage(zip, Path.Combine(session, "payload"), release.Version, cancellation.Token);
            });
            cancellation.Token.ThrowIfCancellationRequested();
            updateState.Text = "Проверено. Перезапускаю программу…";
            string helper = Path.Combine(session, "update-helper.exe"); File.Copy(Path.Combine(Program.Root, "SupertonicVoiceBuilder.exe"), helper);
            var info = new ProcessStartInfo(helper, "--apply-update " + Program.Quote(Program.Root) + " " + Program.Quote(Path.Combine(session, "payload")) + " " + Process.GetCurrentProcess().Id) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = session };
            using (var process = Process.Start(info)) { if (process == null) throw new IOException("Не удалось запустить установку."); }
            EndJob(); Close();
        } catch (OperationCanceledException) { updateState.Text = "Загрузка остановлена. Текущая версия сохранена."; }
        catch (Exception ex) { updateState.Text = "Обновление не установлено: " + ex.Message; Append(updateState.Text); MessageBox.Show(this, updateState.Text, "Обновление", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { if (!IsDisposed) EndJob(); }
    }
}

sealed class ReleasePackage {
    internal Version Version;
    internal string Url;
    internal ModelAsset Asset;
}
sealed class UpdateReceipt {
    public string version { get; set; }
    public List<ModelAsset> files { get; set; }
}
static class UpdateService {
    internal const string Repository = "https://github.com/Voltur792/supertonic-voice-builder";
    internal static Version InstalledVersion { get { return NormalVersion(Assembly.GetExecutingAssembly().GetName().Version); } }
    internal static Version NormalVersion(Version version) { return new Version(version.Major, version.Minor, Math.Max(0, version.Build)); }
    internal static async Task<ReleasePackage> Check(CancellationToken token) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = 2 * 1024 * 1024 }) {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SupertonicVoiceBuilder/" + InstalledVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            using (var response = await client.GetAsync("https://api.github.com/repos/Voltur792/supertonic-voice-builder/releases/latest", token).ConfigureAwait(false)) {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429) throw new IOException("GitHub временно ограничил запросы. Повтори проверку позже.");
                response.EnsureSuccessStatusCode();
                return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }
    }
    internal static ReleasePackage Parse(string json) {
        var doc = Program.Json().DeserializeObject(json) as Dictionary<string, object>;
        if (doc == null || Flag(doc, "draft") || Flag(doc, "prerelease")) throw new InvalidDataException("Релиз не является стабильным опубликованным выпуском.");
        string tag = Text(doc, "tag_name"); Version version;
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$") || !Version.TryParse(tag.TrimStart('v'), out version)) throw new InvalidDataException("Неизвестный формат версии релиза.");
        version = NormalVersion(version);
        if (version <= InstalledVersion) return new ReleasePackage { Version = version };
        string filename = "SupertonicVoiceBuilder-" + version + "-Windows-x64.zip";
        object raw; if (!doc.TryGetValue("assets", out raw) || !(raw is object[])) throw new InvalidDataException("В релизе нет Windows-пакета.");
        foreach (var obj in (object[])raw) {
            var asset = obj as Dictionary<string, object>; if (asset == null || Text(asset, "name") != filename) continue;
            string digest = Text(asset, "digest"), url = Text(asset, "browser_download_url");
            long size = Convert.ToInt64(asset["size"], CultureInfo.InvariantCulture);
            if (!Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$")) throw new InvalidDataException("GitHub не предоставил SHA-256 пакета. Установка недоступна.");
            if (size <= 0 || size > 512L * 1024 * 1024 || url != Repository + "/releases/download/" + tag + "/" + filename) throw new InvalidDataException("Некорректный адрес или размер пакета.");
            return new ReleasePackage { Version = version, Url = url, Asset = new ModelAsset { path = filename, size = size, sha256 = digest.Substring(7).ToLowerInvariant() } };
        }
        throw new InvalidDataException("Для нового релиза пока нет пакета " + filename);
    }
    static bool Flag(Dictionary<string, object> doc, string key) { object value; return doc.TryGetValue(key, out value) && value is bool && (bool)value; }
    static string Text(Dictionary<string, object> doc, string key) { object value; return doc.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : ""; }
    internal static bool Allowed(string name) {
        if (new[] { "SupertonicVoiceBuilder.exe", "model-assets.json", "README.md", "LICENSE.txt", "assets/voice-builder.png", "assets/voice-builder.ico", "engine/supertonic-clone.exe", "engine/onnxruntime.dll", "engine/onnxruntime_providers_shared.dll", "engine/DirectML.dll", "models/speaker_encoder.onnx", "whisper/whisper-cli.exe", "whisper/whisper.dll", "whisper/ggml.dll", "whisper/ggml-base.dll" }.Contains(name, StringComparer.Ordinal)) return true;
        return Regex.IsMatch(name, @"^whisper/ggml-cpu-[a-z0-9]+\.dll$") || Regex.IsMatch(name, @"^licenses/[a-zA-Z0-9._-]+\.txt$");
    }
    // Check each existing ancestor: junctions must not redirect extraction or installation.
    internal static string SafePath(string root, string path) {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); path = Path.GetFullPath(path);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Путь выходит за папку программы.");
        string current = path;
        while (current != null) {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Обновление через ссылку или junction не поддерживается.");
            current = Path.GetDirectoryName(current);
        }
        return path;
    }
    internal static void Stage(string zip, string destination, Version expected, CancellationToken token) {
        if (Directory.Exists(destination)) throw new IOException("Папка распаковки уже существует.");
        Directory.CreateDirectory(destination);
        var receipt = new UpdateReceipt { version = expected.ToString(), files = new List<ModelAsset>() };
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        using (var archive = ZipFile.OpenRead(zip)) {
            if (archive.Entries.Count > 200) throw new InvalidDataException("Слишком много файлов в пакете.");
            foreach (var entry in archive.Entries) {
                token.ThrowIfCancellationRequested(); string name = entry.FullName;
                if (!Allowed(name) || !names.Add(name)) throw new InvalidDataException("Недопустимый файл пакета: " + name);
                total += entry.Length;
                if (entry.Length <= 0 || entry.Length > 300L * 1024 * 1024 || total > 700L * 1024 * 1024) throw new InvalidDataException("Некорректный размер распакованного пакета.");
                string target = SafePath(destination, Path.Combine(destination, name.Replace('/', '\\')));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var input = entry.Open()) using (var output = File.Create(target)) {
                    byte[] buffer = new byte[131072]; int count; long done = 0;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); done += count; if (done > entry.Length) throw new InvalidDataException("Размер файла превышен."); output.Write(buffer, 0, count); }
                    if (done != entry.Length) throw new InvalidDataException("Файл пакета повреждён.");
                }
                receipt.files.Add(new ModelAsset { path = name, size = entry.Length, sha256 = Downloads.Hash(target) });
            }
        }
        ValidatePayload(destination, receipt);
        File.WriteAllText(Path.Combine(destination, "update-receipt.json"), Program.Json().Serialize(receipt), Encoding.UTF8);
    }
    static void ValidatePayload(string folder, UpdateReceipt receipt) {
        if (receipt == null || receipt.files == null || receipt.files.Count > 200) throw new InvalidDataException("Нет корректной ведомости обновления.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in receipt.files) {
            if (!Allowed(file.path) || !names.Add(file.path) || file.size <= 0 || !Regex.IsMatch(file.sha256 ?? "", @"^[a-f0-9]{64}$")) throw new InvalidDataException("Некорректная ведомость обновления.");
            if (!Downloads.Matches(SafePath(folder, Path.Combine(folder, file.path.Replace('/', '\\'))), file)) throw new InvalidDataException("Распакованный файл повреждён: " + file.path);
        }
        foreach (string name in new[] { "SupertonicVoiceBuilder.exe", "model-assets.json", "assets/voice-builder.png", "assets/voice-builder.ico", "engine/supertonic-clone.exe", "engine/onnxruntime.dll", "engine/onnxruntime_providers_shared.dll", "engine/DirectML.dll", "models/speaker_encoder.onnx", "whisper/whisper-cli.exe", "whisper/whisper.dll", "whisper/ggml.dll", "whisper/ggml-base.dll" })
            if (!names.Contains(name)) throw new InvalidDataException("Неполный пакет: нет " + name);
        Version expected; if (!Version.TryParse(receipt.version, out expected) || NormalVersion(AssemblyName.GetAssemblyName(Path.Combine(folder, "SupertonicVoiceBuilder.exe")).Version) != NormalVersion(expected)) throw new InvalidDataException("Версия EXE не совпадает с релизом.");
    }
    internal static void Apply(string root, string payload) {
        root = Path.GetFullPath(root).TrimEnd('\\'); string updates = Path.Combine(root, "updates");
        SafePath(updates, payload);
        var receipt = Program.Json().Deserialize<UpdateReceipt>(File.ReadAllText(Path.Combine(payload, "update-receipt.json")));
        ValidatePayload(payload, receipt);
        string backup = SafePath(updates, Path.Combine(updates, "backup-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(backup);
        var changed = new List<string>(); var existed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in receipt.files) {
            string target = SafePath(root, Path.Combine(root, file.path.Replace('/', '\\')));
            if (File.Exists(target)) { string old = SafePath(backup, Path.Combine(backup, file.path.Replace('/', '\\'))); Directory.CreateDirectory(Path.GetDirectoryName(old)); File.Copy(target, old); existed.Add(file.path); }
        }
        try {
            // EXE first: another running instance fails before the runtime is changed.
            foreach (var file in receipt.files.OrderBy(f => f.path == "SupertonicVoiceBuilder.exe" ? 0 : 1)) {
                string target = SafePath(root, Path.Combine(root, file.path.Replace('/', '\\')));
                Directory.CreateDirectory(Path.GetDirectoryName(target)); string temporary = target + ".update-" + Guid.NewGuid().ToString("N");
                try { File.Copy(Path.Combine(payload, file.path.Replace('/', '\\')), temporary); if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target); changed.Add(file.path); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        } catch (Exception installError) {
            var errors = new List<string>();
            foreach (string name in changed.AsEnumerable().Reverse()) {
                try { string target = SafePath(root, Path.Combine(root, name.Replace('/', '\\'))); if (existed.Contains(name)) File.Copy(Path.Combine(backup, name.Replace('/', '\\')), target, true); else File.Delete(target); }
                catch (Exception ex) { errors.Add(name + ": " + ex.Message); }
            }
            throw new IOException("Установка не завершена. " + (errors.Count == 0 ? "Предыдущие файлы восстановлены. " : "Резервная копия: " + backup + ". Не восстановлены: " + String.Join("; ", errors) + ". ") + installError.Message, installError);
        }
        string errorFile = SafePath(root, Path.Combine(root, "last-update-error.txt")); if (File.Exists(errorFile)) File.Delete(errorFile);
    }
    internal static int RunHelper(string root, string payload, int pid) {
        try {
            SafePath(Path.Combine(root, "updates"), payload);
            try { using (var parent = Process.GetProcessById(pid)) {
                if (!parent.HasExited) {
                    try { if (!String.Equals(parent.MainModule.FileName, Path.Combine(Path.GetFullPath(root), "SupertonicVoiceBuilder.exe"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Неизвестный процесс приложения."); }
                    catch (System.ComponentModel.Win32Exception) { if (!parent.HasExited) throw; }
                    catch (InvalidOperationException) { if (!parent.HasExited) throw; }
                }
                if (!parent.WaitForExit(60000)) throw new IOException("Программа не закрылась. Обновление отменено.");
            } } catch (ArgumentException) { /* Parent already exited. */ }
            Apply(root, payload);
            Process.Start(new ProcessStartInfo(Path.Combine(root, "SupertonicVoiceBuilder.exe")) { WorkingDirectory = root, UseShellExecute = false }); return 0;
        } catch (Exception ex) {
            try { File.WriteAllText(SafePath(root, Path.Combine(root, "last-update-error.txt")), ex.Message, Encoding.UTF8); } catch (IOException) {} catch (UnauthorizedAccessException) {}
            MessageBox.Show(ex.Message, "Обновление не установлено", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
        }
    }
}
