using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Supertonic Voice Builder")]
[assembly: System.Reflection.AssemblyVersion("1.1.3.0")]

static class Program {
    internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    [STAThread]
    static int Main(string[] args) {
        try {
            if (args.Length == 4 && args[0] == "--apply-update") return UpdateService.RunHelper(args[1], args[2], int.Parse(args[3], CultureInfo.InvariantCulture));
            if (args.Length == 2 && args[0] == "--validate") {
                Validate(args[1]);
                File.WriteAllText(Path.Combine(Root, "validation-result.txt"), "OK: Supertonic 3 JSON\n" + args[1], Encoding.UTF8);
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new BuilderForm()) {
                if (args.Length == 2 && args[0] == "--render") {
                    form.Location = new Point(-32000, -32000);
                    form.StartPosition = FormStartPosition.Manual;
                    form.Show(); Application.DoEvents();
                    using (var bitmap = new Bitmap(form.Width, form.Height)) {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(args[1]);
                    }
                    return 0;
                }
                Application.Run(form);
            }
            return 0;
        } catch (Exception ex) {
            File.WriteAllText(Path.Combine(Root, "last-error.txt"), ex.ToString(), Encoding.UTF8);
            if (args.Length == 0) MessageBox.Show(ex.Message, "Supertonic Voice Builder", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }; }
    internal static void Validate(string path) {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length == 0 || file.Length > 8 * 1024 * 1024) throw new InvalidDataException("JSON отсутствует, пуст или слишком велик.");
        var data = Json().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
        if (data == null) throw new InvalidDataException("Некорректный JSON профиля.");
        CheckTensor(data, "style_ttl", new int[] { 1, 50, 256 });
        CheckTensor(data, "style_dp", new int[] { 1, 8, 16 });
    }
    static void CheckTensor(Dictionary<string, object> data, string key, int[] shape) {
        object value; if (!data.TryGetValue(key, out value)) throw new InvalidDataException("Нет " + key);
        var tensor = value as Dictionary<string, object>;
        if (tensor == null || !tensor.ContainsKey("dims") || !tensor.ContainsKey("data")) throw new InvalidDataException("Неверный тензор " + key);
        var dims = tensor["dims"] as object[];
        if (dims == null || dims.Length != shape.Length) throw new InvalidDataException("Неверная размерность " + key);
        for (int i = 0; i < shape.Length; i++) if (Convert.ToInt32(dims[i], CultureInfo.InvariantCulture) != shape[i]) throw new InvalidDataException("Неверная форма " + key);
        CheckValues(tensor["data"], shape, 0, key);
    }
    static void CheckValues(object node, int[] shape, int depth, string key) {
        if (depth == shape.Length) {
            if (!(node is int || node is long || node is double || node is decimal || node is float)) throw new InvalidDataException("Нечисловое значение " + key);
            double number = Convert.ToDouble(node, CultureInfo.InvariantCulture);
            if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number) > 1000000) throw new InvalidDataException("Некорректное значение " + key);
            return;
        }
        var array = node as object[];
        if (array == null || array.Length != shape[depth]) throw new InvalidDataException("Данные не соответствуют dims: " + key);
        foreach (object item in array) CheckValues(item, shape, depth + 1, key);
    }
    internal static string Quote(string value) {
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value) {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') result.Append('\\', slashes * 2 + 1).Append(c);
            else result.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}

sealed partial class BuilderForm : Form {
    readonly TextBox reference = new TextBox(), model = new TextBox(), transcript = new TextBox(), sample = new TextBox(), log = new TextBox();
    readonly StyledCombo language = new StyledCombo(), quality = new StyledCombo(), device = new StyledCombo();
    readonly GrayNumber start = new GrayNumber(), end = new GrayNumber();
    readonly CheckBox window = new GrayCheck();
    readonly Button build = new GlowButton(), stop = new GlowButton(), play = new GlowButton(), folder = new GlowButton();
    readonly Label state = new Label();
    readonly ProgressBar progress = new FlatProgress();
    readonly List<Control> inputs = new List<Control>();
    readonly object processLock = new object();
    Process active;
    SoundPlayer player;
    volatile bool cancelled;
    bool busy;
    string resultJson, resultWav;
    int[] budget;
    readonly Color ink = Color.FromArgb(29, 41, 58);

    internal BuilderForm() {
        BuildLayout();
        inputs.AddRange(new Control[] { reference, model, transcript, sample, language, quality, device, window, start, end });
        build.Click += async delegate { await CreateVoice(); };
        stop.Click += delegate { Cancel(); };
        play.Click += delegate {
            try { if (player != null) player.Dispose(); player = new SoundPlayer(resultWav); player.Play(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Не удалось воспроизвести"); }
        };
        folder.Click += delegate { if (resultJson != null) Process.Start("explorer.exe", Program.Quote(Path.GetDirectoryName(resultJson))); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) {
            if (busy && MessageBox.Show(this, "Подбор ещё идёт. Остановить его и закрыть окно?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) { e.Cancel = true; return; }
            Cancel();
            if (player != null) { player.Stop(); player.Dispose(); }
        };
        LoadDefaults();
        string localModel = Path.Combine(Program.Root, @"models\supertonic-3");
        try { ValidateModel(localModel); model.Text = localModel; } catch (IOException) {} catch (InvalidDataException) {}
    }
    Control PathRow(string title, TextBox input, bool directory) {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 23)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new Label { Text = title, AutoSize = true }; panel.Controls.Add(label, 0, 0); panel.SetColumnSpan(label, 2);
        input.Dock = DockStyle.Fill; panel.Controls.Add(new InputShell(input), 0, 1);
        var button = new GlowButton { Text = "Выбрать…", Dock = DockStyle.Fill, Margin = new Padding(3, 0, 0, 0) }; panel.Controls.Add(button, 1, 1); inputs.Add(button);
        button.Click += delegate {
            if (directory) { using (var dialog = new FolderBrowserDialog { SelectedPath = input.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) input.Text = dialog.SelectedPath; }
            else { using (var dialog = new OpenFileDialog { Filter = "Запись WAV|*.wav", CheckFileExists = true }) if (dialog.ShowDialog(this) == DialogResult.OK) { input.Text = dialog.FileName; transcript.Clear(); window.Checked = false; } }
        };
        return panel;
    }
    Control TextRow(string title, TextBox input) {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 23)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = title, AutoSize = true }); input.Dock = DockStyle.Fill; panel.Controls.Add(new InputShell(input)); return panel;
    }
    void LoadDefaults() {
        model.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"astra\astra\data\models\supertonic-3");
        string path = Path.Combine(Program.Root, "defaults.json");
        if (!File.Exists(path)) return;
        try {
            var d = Program.Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (d.ContainsKey("reference")) reference.Text = Convert.ToString(d["reference"]);
            if (d.ContainsKey("transcript")) transcript.Text = Convert.ToString(d["transcript"]);
            if (d.ContainsKey("start")) start.Value = Convert.ToDecimal(d["start"], CultureInfo.InvariantCulture);
            if (d.ContainsKey("end")) end.Value = Convert.ToDecimal(d["end"], CultureInfo.InvariantCulture);
            window.Checked = transcript.Text.Length > 0;
        } catch (Exception ex) { Append("Не удалось прочитать настройки: " + ex.Message); }
    }
    void Append(string line) {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { try { BeginInvoke(new Action<string>(Append), line); } catch (InvalidOperationException) {} return; }
        if (log.TextLength > 180000) log.Text = log.Text.Substring(log.TextLength - 100000);
        log.AppendText(line + Environment.NewLine);
        var speechProgress = Regex.Match(line, @"progress =\s*(\d+)%");
        if (speechProgress.Success && busy && budget == null) { progress.Value = Math.Min(99, int.Parse(speechProgress.Groups[1].Value)); state.Text = "Распознавание · " + progress.Value + "%"; }
        var match = Regex.Match(line, @"^\[(mix-presets|mix-rows|refine)\]\s+(\d+)/(\d+)");
        if (match.Success && budget != null) {
            int stage = match.Groups[1].Value == "mix-presets" ? 0 : match.Groups[1].Value == "mix-rows" ? 1 : 2;
            int done = int.Parse(match.Groups[2].Value);
            for (int i = 0; i < stage; i++) done += budget[i];
            progress.Value = Math.Min(99, 100 * done / budget.Sum());
            state.Text = "Подбор голоса · " + progress.Value + "%";
        }
    }
    void Cancel() {
        cancelled = true;
        if (cancellation != null) cancellation.Cancel();
        lock (processLock) { if (active != null) { try { if (!active.HasExited) active.Kill(); } catch (InvalidOperationException) {} } }
    }
    int Run(List<string> args, string logPath) { return RunProcess(Path.Combine(Program.Root, @"engine\supertonic-clone.exe"), args, logPath); }
    int RunProcess(string executable, List<string> args, string logPath) {
        if (cancelled) throw new OperationCanceledException();
        var info = new ProcessStartInfo {
            FileName = executable,
            Arguments = string.Join(" ", args.Select(Program.Quote)),
            WorkingDirectory = Path.GetDirectoryName(executable),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        info.EnvironmentVariables["ORT_DYLIB_PATH"] = Path.Combine(Program.Root, @"engine\onnxruntime.dll");
        using (var journal = new StreamWriter(logPath, true, Encoding.UTF8))
        using (var process = new Process { StartInfo = info }) {
            object writeLock = new object();
            DataReceivedEventHandler output = delegate(object sender, DataReceivedEventArgs e) {
                if (e.Data == null) return;
                lock (writeLock) { journal.WriteLine(e.Data); journal.Flush(); } Append(e.Data);
            };
            process.OutputDataReceived += output; process.ErrorDataReceived += output;
            lock (processLock) { if (cancelled) throw new OperationCanceledException(); process.Start(); active = process; }
            try {
                process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
                if (cancelled) throw new OperationCanceledException();
                return process.ExitCode;
            } finally { lock (processLock) { active = null; } }
        }
    }
    async Task CreateVoice() {
        if (busy) return;
        try {
            if (!File.Exists(reference.Text) || !reference.Text.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Выбери существующую запись WAV.");
            foreach (string name in new[] { "duration_predictor.onnx", "text_encoder.onnx", "vector_estimator.onnx", "vocoder.onnx", "tts.json", "unicode_indexer.json" })
                if (!File.Exists(Path.Combine(model.Text, "onnx", name))) throw new InvalidDataException("В папке модели отсутствует onnx\\" + name);
            foreach (string name in new[] { "F1", "F2", "F3", "F4", "F5", "M1", "M2", "M3", "M4", "M5" })
                if (!File.Exists(Path.Combine(model.Text, "voice_styles", name + ".json"))) throw new InvalidDataException("Отсутствует профиль " + name);
            if (initialVoice.Text.Trim().Length > 0) Program.Validate(initialVoice.Text.Trim());
            if (autoTranscript.Checked) CheckWhisper();
            if (window.Checked && transcript.Text.Trim().Length > 0 && end.Value <= start.Value) throw new InvalidDataException("Конец участка должен быть позже начала.");
            if (sample.Text.Trim().Length == 0) throw new InvalidDataException("Введи фразу для проверки.");
            if (!File.Exists(Path.Combine(Program.Root, @"engine\supertonic-clone.exe")) || !File.Exists(Path.Combine(Program.Root, @"models\speaker_encoder.onnx"))) throw new FileNotFoundException("Не найден движок или модель сравнения голосов. Распакуй программу целиком.");
        } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Проверь настройки", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        string destination;
        using (var dialog = new SaveFileDialog { Filter = "Голос Supertonic 3|*.json", AddExtension = true, DefaultExt = "json", FileName = "my_voice.json", InitialDirectory = Path.Combine(Program.Root, "output"), OverwritePrompt = true }) {
            Directory.CreateDirectory(dialog.InitialDirectory);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            destination = Path.GetFullPath(dialog.FileName);
        }
        string protectedRoot = Path.GetFullPath(model.Text).TrimEnd('\\') + "\\";
        if (destination.StartsWith(protectedRoot, StringComparison.OrdinalIgnoreCase) || string.Equals(destination, Path.GetFullPath(reference.Text), StringComparison.OrdinalIgnoreCase)) {
            MessageBox.Show(this, "Сохрани новый профиль в отдельную папку, например output."); return;
        }
        string wavDestination = Path.Combine(Path.GetDirectoryName(destination), Path.GetFileNameWithoutExtension(destination) + ".preview.wav");
        if (string.Equals(wavDestination, Path.GetFullPath(reference.Text), StringComparison.OrdinalIgnoreCase)) {
            MessageBox.Show(this, "Выбери другое имя результата: проверочная озвучка совпадает с исходной записью."); return;
        }
        if (File.Exists(wavDestination) && MessageBox.Show(this, "Заменить существующий пример озвучки " + Path.GetFileName(wavDestination) + "?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        string job = JobFolder(); string staged = Path.Combine(job, "voice.json"), preview = Path.Combine(job, "preview.wav");
        log.Clear(); BeginJob("Загрузка моделей и выбор исходного голоса…");
        try {
            if (autoTranscript.Checked) { budget = null; state.Text = "Распознаю речь локально…"; await TranscribeAudio(job); }
            var cloneArgs = CloneArguments(staged);
            var synthArgs = new List<string> { "--root-models-path", model.Text, "--device", device.SelectedIndex == 0 ? "cpu" : "gpu", "--verbose", "--voice", staged, "--language", LanguageCode(), "--text", sample.Text.Trim(), "--voice-quality", "8", "--output", preview };
            state.Text = "Подбор голоса…";
            await Task.Run(delegate {
                if (Run(cloneArgs, Path.Combine(job, "clone.log")) != 0) throw new InvalidOperationException("Движок не завершил подбор. Подробности на вкладке «Журнал».");
                Program.Validate(staged);
                Append("JSON проверен. Генерирую новую фразу для проверки…");
                if (Run(synthArgs, Path.Combine(job, "preview.log")) != 0) throw new InvalidOperationException("JSON создан, но проверочная озвучка не удалась. Файлы сохранены в " + job);
                if (cancelled) throw new OperationCanceledException();
                File.Copy(staged, destination, true); File.Copy(preview, wavDestination, true);
                File.Copy(Path.Combine(job, "clone.log"), destination + ".log", true);
            });
            resultJson = destination; resultWav = wavDestination; progress.Value = 100;
            state.Text = "Готово. JSON проверен синтезом; послушай сходство с записью.";
            Append("Сохранено: " + destination);
        } catch (OperationCanceledException) { state.Text = "Остановлено. Промежуточный профиль остался в папке runs."; }
        catch (Exception ex) { state.Text = "Не удалось завершить."; Append(ex.Message); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { EndJob(); }
    }
    string LanguageCode() { return language.SelectedIndex == 0 ? "ru" : language.SelectedIndex == 1 ? "en" : "na"; }
    List<string> CloneArguments(string output) {
        string lang = LanguageCode();
        budget = quality.SelectedIndex == 0 ? new[] { 10, 10, 10 } : quality.SelectedIndex == 1 ? new[] { 20, 30, 50 } : new[] { 40, 60, 100 };
        var args = new List<string> { "--root-models-path", model.Text, "--device", device.SelectedIndex == 0 ? "cpu" : "gpu", "--verbose", "clone", "--reference", reference.Text, "--output", output, "--language", lang, "--mix-iters", budget[0].ToString(), "--row-iters", budget[1].ToString(), "--iters", budget[2].ToString(), "--voice-quality", quality.SelectedIndex == 0 ? "5" : "8", "--probes", quality.SelectedIndex == 2 ? "3" : "2", "--directions", quality.SelectedIndex == 2 ? "2" : "1", "--speaker-model", Path.Combine(Program.Root, @"models\speaker_encoder.onnx"), "--save-every", "10" };
        if (initialVoice.Text.Trim().Length > 0) args.AddRange(new[] { "--init-voice", initialVoice.Text.Trim() });
        if (transcript.Text.Trim().Length > 0) {
            args.AddRange(new[] { "--ref-text", transcript.Text.Trim() });
            if (window.Checked) {
                if (end.Value <= start.Value) throw new InvalidDataException("Конец участка должен быть позже начала.");
                args.AddRange(new[] { "--ref-window", start.Value.ToString(CultureInfo.InvariantCulture) + "," + end.Value.ToString(CultureInfo.InvariantCulture) });
            }
        }
        if (lang == "ru" || lang == "na") foreach (string text in new[] { "Привет, я рада тебя слышать.", "Завтра мы продолжим нашу работу.", "Сегодня хороший день для прогулки.", "Расскажи мне, что произошло.", "Я расскажу тебе небольшую историю." }) args.AddRange(new[] { "--probe", text });
        return args;
    }
}
