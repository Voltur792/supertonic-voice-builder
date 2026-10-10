using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed partial class BuilderForm {
    readonly TextBox whisperModel = new TextBox(), initialVoice = new TextBox();
    readonly CheckBox autoTranscript = new GrayCheck(), shortSegment = new GrayCheck();
    readonly Button transcribe = new GlowButton(), downloadTts = new GlowButton(), downloadWhisper = new GlowButton(), theme = new GlowButton();
    readonly StyledCombo whisperChoice = new StyledCombo();
    readonly Label modelState = new Label();
    readonly TabDeck tabs = new TabDeck();
    CancellationTokenSource cancellation;
    bool dark = true;
    Image logo;

    void BuildLayout() {
        Text = "Supertonic Voice Builder · 1.1.3"; ClientSize = new Size(960, 840);
        MinimumSize = new Size(900, 860); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        try { Icon = new Icon(Path.Combine(Program.Root, "assets", "voice-builder.ico")); logo = Image.FromFile(Path.Combine(Program.Root, "assets", "voice-builder.png")); } catch (IOException) {}
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 18), ColumnCount = 1, RowCount = 5 };
        foreach (int h in new[] { 84, 0, 62, 6, 36 }) layout.RowStyles.Add(new RowStyle(h == 0 ? SizeType.Percent : SizeType.Absolute, h == 0 ? 100 : h));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(layout);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        header.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(64, 64), Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label { Text = "Создай свой голос", AutoSize = true, Font = new Font("Segoe UI", 24, FontStyle.Bold), Location = new Point(0, 0) });
        heading.Controls.Add(new Label { Text = "WAV → профиль Supertonic 3 → новая речь", AutoSize = true, Location = new Point(2, 48), Tag = "muted" });
        header.Controls.Add(heading, 1, 0); theme.Text = "Светлая тема"; theme.Size = new Size(150, 35); header.Controls.Add(theme, 2, 0); layout.Controls.Add(header, 0, 0);
        tabs.Dock = DockStyle.Fill;
        var voiceTab = new TabSheet("Голос"); var modelsTab = new TabSheet("Модели"); var updatesTab = new TabSheet("Обновления"); var journalTab = new TabSheet("Журнал");
        tabs.TabSheets.AddRange(new[] { voiceTab, modelsTab, updatesTab, journalTab }); layout.Controls.Add(tabs, 0, 1);
        BuildUpdates(updatesTab);
        var content = Rows(voiceTab, new[] { 64, 64, 44, 88, 37, 38, 64, 66, 56 });
        content.Controls.Add(PathRow("1  Запись голоса · WAV", reference, false));
        content.Controls.Add(PathRow("Модель Supertonic 3 · можно скачать на вкладке «Модели»", model, true));
        var choices = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        language.DropDownStyle = quality.DropDownStyle = device.DropDownStyle = ComboBoxStyle.DropDownList;
        language.Items.AddRange(new object[] { "Русский (ru)", "English (en)", "Автоязык (na)" }); language.SelectedIndex = 0; language.Width = 165;
        quality.Items.AddRange(new object[] { "Пробный · 30 шагов", "Обычный · 100 шагов", "Тщательный · 200 шагов" }); quality.SelectedIndex = 1; quality.Width = 235;
        device.Items.AddRange(new object[] { "Процессор", "Видеокарта · DirectML" }); device.SelectedIndex = 0; device.Width = 205;
        choices.Controls.AddRange(new Control[] { language, quality, device }); content.Controls.Add(choices);
        transcript.Multiline = true; transcript.ScrollBars = ScrollBars.Vertical; content.Controls.Add(TextRow("2  Текст записи · можно ввести или распознать локально", transcript));
        var asr = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        transcribe.Text = "Распознать запись"; transcribe.Size = new Size(180, 31); autoTranscript.Text = "Распознавать перед созданием"; autoTranscript.AutoSize = true; autoTranscript.Margin = new Padding(15, 6, 0, 0);
        asr.Controls.AddRange(new Control[] { transcribe, autoTranscript }); content.Controls.Add(asr);
        var range = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        window.Text = "Текст участка"; window.AutoSize = true;
        foreach (var n in new[] { start, end }) { n.DecimalPlaces = 2; n.Maximum = 86400; n.Increment = .1M; n.Width = 80; }
        end.Value = 10;
        range.Controls.AddRange(new Control[] { window, new Label { Text = "от, сек", AutoSize = true, Margin = new Padding(8, 7, 3, 0) }, start, new Label { Text = "до, сек", AutoSize = true, Margin = new Padding(8, 7, 3, 0) }, end }); content.Controls.Add(range);
        content.Controls.Add(FileRow("Продолжить подбор из JSON · необязательно", initialVoice, "Профиль голоса|*.json", true));
        sample.Multiline = true; sample.Text = "Привет! Это мой новый голос для Астры. Сегодня всё обязательно получится."; content.Controls.Add(TextRow("3  Фраза для проверки результата", sample));
        content.Controls.Add(new Label { Text = "Тщательный режим: 8 шагов синтеза, 3 фразы и усреднение поиска.\nСходство зависит от записи. Лучше чистая речь одного человека без музыки.", AutoSize = true, Tag = "muted" });
        var models = Rows(modelsTab, new[] { 34, 62, 46, 30, 42, 62, 42, 43, 70 });
        models.Controls.Add(new Label { Text = "Supertonic 3", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        models.Controls.Add(new Label { Text = "Модель с Hugging Face · около 400 МБ.\nЗагрузка в models\\supertonic-3 рядом с программой. Каждый файл проверяется по SHA-256.", AutoSize = true, Tag = "muted" });
        var ttsActions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        downloadTts.Text = "Скачать Supertonic 3"; downloadTts.Size = new Size(230, 35);
        var license = new LinkLabel { Text = "Модель и лицензия OpenRAIL-M", AutoSize = true, Margin = new Padding(16, 9, 0, 0), LinkColor = Accent, ActiveLinkColor = Accent };
        license.LinkClicked += delegate { Process.Start("https://huggingface.co/supertone-oss-archive/supertonic-3/blob/" + Downloads.SupertonicRevision + "/LICENSE"); };
        ttsActions.Controls.AddRange(new Control[] { downloadTts, license }); models.Controls.Add(ttsActions); modelState.AutoSize = true; models.Controls.Add(modelState);
        models.Controls.Add(new Label { Text = "Whisper · необязательное распознавание", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 10, 0, 0) });
        models.Controls.Add(FileRow("Модель Whisper · существующий файл Astra также подойдёт", whisperModel, "Модель Whisper|ggml-*.bin|Все файлы|*.*", false));
        var whisperActions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        whisperChoice.DropDownStyle = ComboBoxStyle.DropDownList; whisperChoice.Items.AddRange(new object[] { "Base · 148 МБ · быстрее", "Turbo q5 · 574 МБ · точнее" }); whisperChoice.SelectedIndex = 0; whisperChoice.Width = 275;
        downloadWhisper.Text = "Скачать Whisper"; downloadWhisper.Size = new Size(200, 33); whisperActions.Controls.AddRange(new Control[] { whisperChoice, downloadWhisper }); models.Controls.Add(whisperActions);
        shortSegment.Text = "После распознавания выбрать короткий участок 4–12 секунд"; shortSegment.AutoSize = true; shortSegment.Checked = true; models.Controls.Add(shortSegment);
        models.Controls.Add(new Label { Text = "Распознавание работает локально и запускается только по твоему выбору.\nПроверь текст перед подбором: точная транскрипция помогает согласовать темп и произношение.\nЕсли модель уже есть в Astra, повторная загрузка не нужна.", AutoSize = true, Tag = "muted" });
        log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.Dock = DockStyle.Fill; log.Font = new Font("Consolas", 9); journalTab.Padding = new Padding(12); journalTab.Controls.Add(new InputShell(log));
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Padding = new Padding(0, 10, 0, 6) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        build.Text = "Создать JSON"; stop.Text = "Остановить"; stop.Enabled = false;
        play.Text = "Прослушать"; play.Enabled = false; folder.Text = "Папка результата"; folder.Enabled = false;
        foreach (var b in new[] { build, stop, play, folder }) { b.Dock = DockStyle.Fill; b.Margin = new Padding(0, 0, 4, 0); }
        buttons.Controls.Add(build, 0, 0); buttons.Controls.Add(stop, 1, 0); buttons.Controls.Add(play, 3, 0); buttons.Controls.Add(folder, 4, 0); layout.Controls.Add(buttons, 0, 2);
        progress.Dock = DockStyle.Fill; layout.Controls.Add(progress, 0, 3); state.Text = "Выбери запись. Подбор и распознавание выполняются на компьютере."; state.Dock = DockStyle.Fill; state.Padding = new Padding(0, 7, 0, 0); layout.Controls.Add(state, 0, 4);
        inputs.AddRange(new Control[] { whisperModel, initialVoice, autoTranscript, shortSegment, transcribe, downloadTts, downloadWhisper, whisperChoice });
        transcribe.Click += async delegate { await TranscribeOnly(); };
        downloadTts.Click += async delegate { await DownloadModels(false); };
        downloadWhisper.Click += async delegate { await DownloadModels(true); };
        theme.Click += delegate { dark = !dark; ApplyTheme(); SaveTheme(); };
        string astraWhisper = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"astra\astra\data\models\whisper\ggml-large-v3-turbo-q5_0.bin");
        string localWhisper = Path.Combine(Program.Root, @"models\whisper\ggml-base.bin");
        whisperModel.Text = File.Exists(astraWhisper) ? astraWhisper : File.Exists(localWhisper) ? localWhisper : "";
        string localTurbo = Path.Combine(Program.Root, @"models\whisper\ggml-large-v3-turbo-q5_0.bin");
        if (File.Exists(localTurbo)) whisperModel.Text = localTurbo;
        string whisperPreference = Path.Combine(Program.Root, "whisper-model.txt");
        try { if (File.Exists(whisperPreference)) { string saved = File.ReadAllText(whisperPreference).Trim(); if (File.Exists(saved)) whisperModel.Text = saved; } } catch (IOException) {}
        whisperModel.TextChanged += delegate { try { File.WriteAllText(whisperPreference, whisperModel.Text); } catch (IOException) {} catch (UnauthorizedAccessException) {} };
        try { string pref = Path.Combine(Program.Root, "theme.txt"); if (File.Exists(pref)) dark = File.ReadAllText(pref).Trim() != "light"; } catch (IOException) {}
        ApplyTheme();
        FormClosed += delegate { if (logo != null) logo.Dispose(); if (cancellation != null) cancellation.Dispose(); };
    }
    TableLayoutPanel Rows(TabSheet parent, int[] heights) {
        var container = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14) }; parent.Controls.Add(container);
        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = heights.Length, Height = heights.Sum(), Padding = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); foreach (int h in heights) table.RowStyles.Add(new RowStyle(SizeType.Absolute, h)); container.Controls.Add(table); return table;
    }
    Control FileRow(string title, TextBox input, string filter, bool clear) {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = clear ? 3 : 2, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); if (clear) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 23)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new Label { Text = title, AutoSize = true }; panel.Controls.Add(label, 0, 0); panel.SetColumnSpan(label, clear ? 3 : 2);
        input.Dock = DockStyle.Fill; panel.Controls.Add(new InputShell(input), 0, 1); var choose = new GlowButton { Text = "Выбрать…", Dock = DockStyle.Fill, Margin = new Padding(3, 0, 0, 0) }; panel.Controls.Add(choose, 1, 1); inputs.Add(choose);
        choose.Click += delegate { using (var d = new OpenFileDialog { Filter = filter, CheckFileExists = true }) if (d.ShowDialog(this) == DialogResult.OK) input.Text = d.FileName; };
        if (clear) { var reset = new GlowButton { Text = "Сбросить", Dock = DockStyle.Fill, Margin = new Padding(3, 0, 0, 0) }; reset.Click += delegate { input.Clear(); }; panel.Controls.Add(reset, 2, 1); inputs.Add(reset); } return panel;
    }
    Color Accent { get { return dark ? Color.FromArgb(75, 224, 199) : Color.FromArgb(33, 190, 167); } }
    Color Surface { get { return dark ? Color.FromArgb(25, 34, 47) : Color.FromArgb(244, 247, 250); } }
    void ApplyTheme() {
        BackColor = dark ? Color.FromArgb(16, 23, 34) : Color.FromArgb(235, 240, 245); ForeColor = dark ? Color.FromArgb(229, 237, 246) : Color.FromArgb(29, 41, 58);
        ThemeControl(this); theme.Text = dark ? "Светлая тема" : "Тёмная тема"; tabs.RefreshTheme(Accent, Surface);
    }
    void ThemeControl(Control control) {
        if (control != this) { control.BackColor = Surface; control.ForeColor = Equals(control.Tag, "muted") ? (dark ? Color.FromArgb(159, 178, 198) : Color.FromArgb(78, 99, 121)) : ForeColor; }
        if (control is TextBox || control is StyledCombo || control is GrayNumber) control.BackColor = dark ? Color.FromArgb(34, 46, 62) : Color.White;
        var choice = control as StyledCombo; if (choice != null) { choice.Accent = Accent; choice.Border = dark ? Color.FromArgb(54, 72, 94) : Color.FromArgb(197, 211, 224); choice.Invalidate(); }
        var checkbox = control as GrayCheck; if (checkbox != null) { checkbox.Accent = Accent; checkbox.Border = dark ? Color.FromArgb(93, 112, 134) : Color.FromArgb(148, 166, 186); checkbox.Invalidate(); }
        var spinner = control as GrayNumber; if (spinner != null) { spinner.Spin = dark ? Color.FromArgb(53, 67, 84) : Color.FromArgb(217, 226, 235); spinner.Arrow = ForeColor; spinner.Border = dark ? Color.FromArgb(54, 72, 94) : Color.FromArgb(197, 211, 224); spinner.Accent = Accent; spinner.RefreshSpin(); }
        var scrollbar = control as GrayScroll; if (scrollbar != null) { scrollbar.BackColor = dark ? Color.FromArgb(34, 46, 62) : Color.White; scrollbar.Thumb = dark ? Color.FromArgb(78, 94, 114) : Color.FromArgb(164, 181, 199); scrollbar.Hover = dark ? Color.FromArgb(110, 131, 153) : Color.FromArgb(132, 155, 178); scrollbar.Invalidate(); }
        var shell = control as InputShell;
        if (shell != null) { shell.BackColor = dark ? Color.FromArgb(34, 46, 62) : Color.White; shell.BorderColor = dark ? Color.FromArgb(54, 72, 94) : Color.FromArgb(197, 211, 224); shell.Accent = Accent; shell.Invalidate(); }
        var glowButton = control as GlowButton; if (glowButton != null) glowButton.GlowColor = Accent;
        var bar = control as FlatProgress; if (bar != null) { bar.Accent = Accent; bar.Track = dark ? Color.FromArgb(38, 52, 69) : Color.FromArgb(207, 218, 230); bar.Invalidate(); }
        var b = control as Button;
        if (b != null) { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = dark ? Color.FromArgb(61, 82, 103) : Color.FromArgb(189, 205, 218); if (b == build) { b.BackColor = Accent; b.ForeColor = Color.FromArgb(10, 32, 34); b.FlatAppearance.BorderSize = 0; } }
        foreach (Control child in control.Controls) ThemeControl(child);
    }
    void SaveTheme() { try { File.WriteAllText(Path.Combine(Program.Root, "theme.txt"), dark ? "dark" : "light"); } catch (IOException ex) { Append("Тема применена, но не сохранена: " + ex.Message); } }
    void BeginJob(string text) {
        busy = true; cancelled = false; if (cancellation != null) cancellation.Dispose(); cancellation = new CancellationTokenSource();
        foreach (Control input in inputs) input.Enabled = false; build.Enabled = play.Enabled = folder.Enabled = false; stop.Enabled = true; progress.Value = 0; state.Text = text;
    }
    void EndJob() {
        busy = false; stop.Enabled = false; build.Enabled = true; foreach (Control input in inputs) input.Enabled = true;
        play.Enabled = resultWav != null && File.Exists(resultWav); folder.Enabled = resultJson != null && File.Exists(resultJson);
        installUpdate.Enabled = availableUpdate != null;
    }
    string JobFolder() { string path = Path.Combine(Program.Root, "runs", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(path); return path; }
    async Task DownloadModels(bool whisper) {
        if (busy) return;
        bool turbo = whisperChoice.SelectedIndex == 1;
        string destination = Path.Combine(Program.Root, "models", whisper ? "whisper" : "supertonic-3");
        var assets = whisper ? new List<ModelAsset> { Downloads.WhisperAsset(turbo) } : Downloads.SupertonicAssets();
        long total = assets.Sum(a => a.size), completed = 0;
        BeginJob("Подключение к Hugging Face…"); budget = null;
        try {
            var report = new Progress<Tuple<string, long>>(p => { progress.Value = Math.Min(99, (int)(100 * p.Item2 / total)); state.Text = "Загрузка · " + progress.Value + "% · " + p.Item1; });
            await Task.Run(async delegate {
                foreach (var asset in assets) {
                    string target = Path.GetFullPath(Path.Combine(destination, asset.path.Replace('/', '\\')));
                    if (!target.StartsWith(Path.GetFullPath(destination).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Некорректный путь модели.");
                    string url = "https://huggingface.co/" + (whisper ? "ggerganov/whisper.cpp/resolve/" + Downloads.WhisperRevision : "supertone-oss-archive/supertonic-3/resolve/" + Downloads.SupertonicRevision) + "/" + asset.path;
                    Append("Загрузка / проверка: " + asset.path);
                    long before = completed;
                    await Downloads.Fetch(url, target, asset, cancellation.Token, done => ((IProgress<Tuple<string, long>>)report).Report(Tuple.Create(asset.path, before + done)));
                    completed += asset.size;
                }
            });
            if (whisper) whisperModel.Text = Path.Combine(destination, assets[0].path); else { ValidateModel(destination); model.Text = destination; modelState.Text = "Модель загружена и проверена."; }
            progress.Value = 100; state.Text = "Готово. Все файлы проверены по SHA-256.";
        } catch (OperationCanceledException) { state.Text = "Загрузка остановлена. Проверенные файлы сохранены; повторный запуск продолжит загрузку."; }
        catch (Exception ex) { state.Text = "Загрузка не завершена."; Append(ex.Message); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { EndJob(); }
    }
    static void ValidateModel(string path) {
        foreach (string name in new[] { "duration_predictor.onnx", "text_encoder.onnx", "vector_estimator.onnx", "vocoder.onnx", "tts.json", "unicode_indexer.json" })
            if (!File.Exists(Path.Combine(path, "onnx", name))) throw new InvalidDataException("В модели отсутствует onnx\\" + name);
        foreach (string name in new[] { "F1", "F2", "F3", "F4", "F5", "M1", "M2", "M3", "M4", "M5" }) Program.Validate(Path.Combine(path, "voice_styles", name + ".json"));
    }
    async Task TranscribeOnly() {
        if (busy) return;
        try { CheckWhisper(); } catch (Exception ex) { tabs.SelectedIndex = 1; MessageBox.Show(this, ex.Message, "Распознавание"); return; }
        BeginJob("Распознаю речь локально…"); budget = null;
        try { await TranscribeAudio(JobFolder()); progress.Value = 100; state.Text = "Текст распознан. Проверь слова и границы участка перед созданием голоса."; tabs.SelectedIndex = 0; }
        catch (OperationCanceledException) { state.Text = "Распознавание остановлено."; }
        catch (Exception ex) { Append(ex.Message); state.Text = "Распознавание не завершено."; MessageBox.Show(this, ex.Message, "Распознавание"); }
        finally { EndJob(); }
    }
    void CheckWhisper() {
        if (!File.Exists(reference.Text)) throw new FileNotFoundException("Выбери WAV с записью голоса.");
        if (!File.Exists(whisperModel.Text)) throw new FileNotFoundException("Выбери или скачай модель Whisper на вкладке «Модели».");
        if (!File.Exists(Path.Combine(Program.Root, @"whisper\whisper-cli.exe"))) throw new FileNotFoundException("Не найден whisper-cli. Распакуй программу целиком.");
    }
    async Task TranscribeAudio(string job) {
        CheckWhisper(); string input = reference.Text, modelPath = whisperModel.Text, lang = language.SelectedIndex == 0 ? "ru" : language.SelectedIndex == 1 ? "en" : "auto";
        string audio = Path.Combine(job, "whisper-input.wav"), output = Path.Combine(job, "transcript");
        bool selectShort = shortSegment.Checked;
        await Task.Run(delegate {
            WaveAudio.ForWhisper(input, audio, cancellation.Token);
            if (RunTool(Path.Combine(Program.Root, @"whisper\whisper-cli.exe"), new List<string> { "-m", modelPath, "-f", audio, "-l", lang, "-ojf", "-of", output, "-t", "8", "-ng", "-sns", "-pp" }, Path.Combine(job, "whisper.log")) != 0) throw new InvalidOperationException("Whisper не завершил распознавание. Проверь журнал.");
        });
        cancellation.Token.ThrowIfCancellationRequested();
        var doc = Program.Json().DeserializeObject(File.ReadAllText(output + ".json", Encoding.UTF8)) as Dictionary<string, object>;
        var segments = TranscriptSelection.Read(doc);
        var selection = TranscriptSelection.Select(segments, selectShort);
        transcript.Text = selection.Text; window.Checked = selectShort && selection.End > selection.Start;
        if (window.Checked) { start.Value = Math.Min(start.Maximum, (decimal)selection.Start); end.Value = Math.Min(end.Maximum, (decimal)selection.End); }
        Append("Полная транскрипция: " + output + ".json"); Append("Для подбора: " + transcript.Text);
    }
    int RunTool(string executable, List<string> args, string logPath) {
        return RunProcess(executable, args, logPath);
    }
}

sealed class TranscriptPart {
    internal string Text;
    internal double Start, End;
}
static class TranscriptSelection {
    internal static List<TranscriptPart> Read(Dictionary<string, object> doc) {
        object raw; if (doc == null || !doc.TryGetValue("transcription", out raw) || !(raw is object[])) throw new InvalidDataException("Нет транскрипции в ответе Whisper.");
        var list = new List<TranscriptPart>();
        foreach (var item in (object[])raw) {
            var segment = item as Dictionary<string, object>; if (segment == null) continue;
            var offsets = segment["offsets"] as Dictionary<string, object>; string text = Convert.ToString(segment["text"]).Trim();
            if (offsets == null || text.Length == 0 || text.StartsWith("[")) continue;
            double from = Convert.ToDouble(offsets["from"], CultureInfo.InvariantCulture) / 1000, to = Convert.ToDouble(offsets["to"], CultureInfo.InvariantCulture) / 1000;
            if (!Double.IsNaN(from) && !Double.IsInfinity(from) && !Double.IsNaN(to) && !Double.IsInfinity(to) && from >= 0 && to > from) list.Add(new TranscriptPart { Text = text, Start = from, End = to });
        }
        if (list.Count == 0) throw new InvalidDataException("Whisper не обнаружил разборчивую речь."); return list;
    }
    internal static TranscriptPart Select(List<TranscriptPart> list, bool shortClip) {
        if (!shortClip) return new TranscriptPart { Text = String.Join(" ", list.Select(p => p.Text)), Start = list[0].Start, End = list[list.Count - 1].End };
        TranscriptPart best = null; double score = Double.NegativeInfinity;
        for (int i = 0; i < list.Count; i++) {
            string text = "";
            for (int j = i; j < list.Count; j++) {
                if (j > i && list[j].Start - list[j - 1].End > 1.5) break;
                text = (text + " " + list[j].Text).Trim(); double duration = list[j].End - list[i].Start;
                if (duration > 12) break;
                if (text.Length < 12) continue;
                // Prefer a complete sentence around seven seconds, penalise tiny clips.
                double value = Math.Min(text.Length, 100) / 100.0 - Math.Abs(duration - 7) / 8.0 - (duration < 4 ? 2 : 0);
                if (value > score) { score = value; best = new TranscriptPart { Text = text, Start = list[i].Start, End = list[j].End }; }
            }
        }
        // Long indivisible segments retain their full text and exact boundaries.
        return best ?? list.OrderBy(p => p.End - p.Start).First();
    }
}
