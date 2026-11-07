using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UhfTagAnalyzer
{
    public class MainForm : Form
    {
        CF601Reader _reader;
        AnalysisResult _last;
        byte[] _currentEpc;      // aktualne vybrany tag (sdileno mezi zalozkami)
        System.Windows.Forms.Timer _scanTimer;
        readonly Dictionary<string, TagRecord> _scanTags = new Dictionary<string, TagRecord>();
        volatile bool _busy;

        Button _btnConnect, _btnDisconnect;
        StatusLed _led;
        TabControl _tabs;
        TextBox _log;
        ToolStripStatusLabel _lblState, _lblDevice, _lblTag;

        public MainForm()
        {
            Text = "CF601 UHF Tag Analyzer";
            Width = 1140; Height = 800;
            MinimumSize = new Size(980, 660);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            BackColor = Theme.Bg;
            try { using var st = GetType().Assembly.GetManifestResourceStream("UhfTagAnalyzer.app.ico"); if (st != null) Icon = new Icon(st); } catch { }
            BuildShell();
            SetConnected(false);
            Log("Pripoj ctecku CF601 pres USB a klikni 'Pripojit'.");
        }

        // ============================ SHELL ============================
        void BuildShell()
        {
            BackColor = Theme.Bg;

            var header = new HeaderBar();

            var top = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(12, 9, 10, 9), BackColor = Theme.Bg };
            var topFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg, WrapContents = false };
            _led = new StatusLed { Margin = new Padding(0, 2, 6, 2) };
            _btnConnect = new SoftButton(SoftButton.Kind.Success) { Text = "Pripojit" }; ((SoftButton)_btnConnect).FitWidth(); _btnConnect.Click += (s, e) => Connect();
            _btnDisconnect = new SoftButton(SoftButton.Kind.Danger) { Text = "Odpojit" }; ((SoftButton)_btnDisconnect).FitWidth(); _btnDisconnect.Click += (s, e) => Disconnect();
            topFlow.Controls.AddRange(new Control[] { _led, _btnConnect, _btnDisconnect });
            top.Controls.Add(topFlow);

            _tabs = new TabControl { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            Theme.StyleTabs(_tabs);
            _tabs.TabPages.Add(TabAnalyza());
            _tabs.TabPages.Add(TabScan());
            _tabs.TabPages.Add(TabMemory());
            _tabs.TabPages.Add(TabTests());
            _tabs.TabPages.Add(TabConfig());
            _tabs.TabPages.Add(TabOps());
            foreach (TabPage p in _tabs.TabPages) p.BackColor = Theme.Bg;

            var logWrap = new Card("Log") { Dock = DockStyle.Bottom, Height = 118, Margin = new Padding(0) };
            _log = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8.5f),
                BackColor = Theme.LogBg, ForeColor = Theme.LogText, BorderStyle = BorderStyle.None
            };
            logWrap.Controls.Add(_log);

            var status = new StatusStrip { BackColor = Theme.Surface, SizingGrip = false, ForeColor = Theme.Muted };
            _lblState = new ToolStripStatusLabel("Odpojeno") { ForeColor = Theme.Bad, Font = new Font("Segoe UI Semibold", 9f) };
            _lblTag = new ToolStripStatusLabel("Tag: -") { ForeColor = Theme.Muted };
            _lblDevice = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Muted };
            var sig = new ToolStripStatusLabel("Hesse Valentino · hesse.works") { ForeColor = Theme.Accent, IsLink = true, Font = new Font("Segoe UI", 8.5f) };
            sig.Click += (s, e) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://hesse.works") { UseShellExecute = true }); } catch { } };
            status.Items.AddRange(new ToolStripItem[] { _lblState, new ToolStripStatusLabel("·") { ForeColor = Theme.Faint }, _lblTag, _lblDevice, sig });

            Controls.Add(_tabs);
            Controls.Add(logWrap);
            Controls.Add(top);
            Controls.Add(header);
            Controls.Add(status);

            foreach (TabPage p in _tabs.TabPages) ApplyTheme(p);
        }

        // sjednoti pozadi kontejneru a popisku s tematem (nechava GroupBox/vstupy/tlacitka)
        void ApplyTheme(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is Card) { /* karta si kresli pozadi sama */ }
                else if (c is SplitContainer sc) { sc.BackColor = Theme.Bg; sc.Panel1.BackColor = Theme.Bg; sc.Panel2.BackColor = Theme.Bg; }
                else if (c is FlowLayoutPanel || (c.GetType() == typeof(Panel)))
                    c.BackColor = Theme.Bg;
                else if (c is Label && !(c is LinkLabel))
                { c.BackColor = Color.Transparent; if (c.ForeColor == SystemColors.ControlText) c.ForeColor = Theme.Ink; }
                if (c.HasChildren) ApplyTheme(c);
            }
        }

        Button Btn(string text, EventHandler onClick, bool primary = false, bool danger = false)
        {
            var kind = danger ? SoftButton.Kind.Danger : primary ? SoftButton.Kind.Primary : SoftButton.Kind.Secondary;
            var b = new SoftButton(kind) { Text = text };
            b.FitWidth();
            b.Click += onClick;
            return b;
        }

        RichTextBox OutBox() => new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, Font = Theme.MonoFont, BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface, ForeColor = Theme.Ink, WordWrap = false, DetectUrls = false
        };

        // ============================ TAB: ANALYZA ============================
        ListView _tagList; RichTextBox _detail; NumericUpDown _scanDur;
        TabPage TabAnalyza()
        {
            var tp = new TabPage("Analyza");
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(6, 8, 6, 6) };
            var bAnalyze = Btn("Analyzuj tag", (s, e) => Analyze(), primary: true);
            var bSave = Btn("Ulozit report", (s, e) => SaveReport());
            var lbl = new Label { Text = "Inventura (s):", AutoSize = true, Padding = new Padding(10, 7, 2, 0) };
            _scanDur = new NumericUpDown { Minimum = 0.3M, Maximum = 10M, Increment = 0.5M, DecimalPlaces = 1, Value = 1.0M, Width = 60 };
            bar.Controls.AddRange(new Control[] { bAnalyze, bSave, lbl, _scanDur });

            _tagList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            _tagList.Columns.Add("EPC", 260); _tagList.Columns.Add("RSSI", 55); _tagList.Columns.Add("Cip", 160);
            _tagList.SelectedIndexChanged += (s, e) => { if (_tagList.SelectedItems.Count > 0 && _tagList.SelectedItems[0].Tag is AnalysisResult r) { _detail.Text = r.Report; Colorize(r); } };
            _detail = OutBox();

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 300 };
            split.Panel1.Controls.Add(Group("Detekovane tagy", _tagList));
            split.Panel2.Controls.Add(Group("Analyza", _detail));

            tp.Controls.Add(split); tp.Controls.Add(bar);
            return tp;
        }

        Card Group(string t, Control c)
        {
            var card = new Card(t) { Dock = DockStyle.Fill };
            if (c is RichTextBox || c is ListView) c.Font = c is RichTextBox ? Theme.MonoFont : Theme.UiFont;
            if (c is ListView lv) { lv.BorderStyle = BorderStyle.None; lv.BackColor = Theme.Surface; }
            c.Dock = DockStyle.Fill;
            card.Controls.Add(c);
            return card;
        }

        async void Analyze()
        {
            if (!Ready()) return;
            _tagList.Items.Clear(); _detail.Clear();
            double scan = (double)_scanDur.Value;
            await RunBusy("Analyza", async () =>
            {
                var res = await Task.Run(() => _reader.Analyze(scan, 6, 64, UiLog));
                _last = res;
                BeginInvoke((Action)(() =>
                {
                    _detail.Text = res.Report ?? "";
                    Colorize(res);
                    _tagList.Items.Clear();
                    if (res.Detected && res.Tag != null)
                    {
                        _currentEpc = res.Tag.Epc;
                        _lblTag.Text = "Tag: " + res.Tag.EpcHex;
                        var it = new ListViewItem(new[] { res.Tag.EpcHex, res.Tag.Rssi.ToString("0.0"), res.Tid?.Summary ?? "?" }) { Tag = res };
                        _tagList.Items.Add(it); it.Selected = true;
                    }
                }));
            });
        }

        void Colorize(AnalysisResult res)
        {
            if (res == null || string.IsNullOrEmpty(res.Report)) return;
            int idx = _detail.Text.IndexOf("ZAVER:", StringComparison.Ordinal);
            if (idx < 0) return;
            int end = _detail.Text.IndexOf('\n', idx); if (end < 0) end = _detail.Text.Length;
            _detail.Select(idx, end - idx);
            _detail.SelectionColor = res.Verdict.StartsWith("FUNKCNI") ? Color.ForestGreen : res.Verdict.StartsWith("CASTECNE") ? Color.DarkOrange : Color.Firebrick;
            _detail.SelectionFont = new Font(_detail.Font, FontStyle.Bold);
            _detail.Select(0, 0);
        }

        void SaveReport()
        {
            if (_last == null || string.IsNullOrEmpty(_last.Report)) { Log("Neni co ulozit."); return; }
            using var dlg = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", FileName = "tag-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" };
            if (dlg.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(dlg.FileName, _last.Report); Log("Ulozeno: " + dlg.FileName); }
        }

        // ============================ TAB: SKEN / CSV ============================
        ListView _scanList; Button _bScanStart, _bScanStop;
        TabPage TabScan()
        {
            var tp = new TabPage("Sken / CSV");
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(6, 8, 6, 6) };
            _bScanStart = Btn("Start skenu", (s, e) => ScanStart());
            _bScanStop = Btn("Stop", (s, e) => ScanStop()); _bScanStop.Enabled = false;
            var bClear = Btn("Vycistit", (s, e) => { _scanTags.Clear(); _scanList.Items.Clear(); });
            var bCsv = Btn("Export CSV", (s, e) => ExportCsv());
            bar.Controls.AddRange(new Control[] { _bScanStart, _bScanStop, bClear, bCsv });

            _scanList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            _scanList.Columns.Add("EPC", 300); _scanList.Columns.Add("Cteni", 60); _scanList.Columns.Add("RSSI", 60);
            _scanList.Columns.Add("Ant", 40); _scanList.Columns.Add("Kanal", 50); _scanList.Columns.Add("Naposledy", 90);

            tp.Controls.Add(Group("Nasbirane tagy (prubezny sken)", _scanList)); tp.Controls.Add(bar);
            return tp;
        }

        void ScanStart()
        {
            if (!Ready()) return;
            _bScanStart.Enabled = false; _bScanStop.Enabled = true;
            Log("Prubezny sken spusten.");
            _scanTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _scanTimer.Tick += (s, e) => ScanTick();
            _scanTimer.Start();
        }

        void ScanTick()
        {
            if (_busy || _reader == null || !_reader.IsOpen) return;
            _busy = true;
            Task.Run(() =>
            {
                try
                {
                    var found = _reader.Inventory(0.25, out _);
                    BeginInvoke((Action)(() =>
                    {
                        foreach (var kv in found)
                        {
                            if (_scanTags.TryGetValue(kv.Key, out var r)) { r.Count += kv.Value.Count; r.Rssi = kv.Value.Rssi; }
                            else _scanTags[kv.Key] = kv.Value;
                            UpsertScanRow(kv.Key, _scanTags[kv.Key]);
                        }
                    }));
                }
                catch (Exception ex) { UiLog("sken chyba: " + ex.Message); }
                finally { _busy = false; }
            });
        }

        void UpsertScanRow(string epc, TagRecord r)
        {
            var existing = _scanList.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Text == epc);
            string[] vals = { epc, r.Count.ToString(), r.Rssi.ToString("0.0"), r.Antenna.ToString(), r.Channel.ToString(), DateTime.Now.ToString("HH:mm:ss") };
            if (existing != null) { for (int i = 0; i < vals.Length; i++) existing.SubItems[i].Text = vals[i]; }
            else _scanList.Items.Add(new ListViewItem(vals));
        }

        void ScanStop()
        {
            _scanTimer?.Stop(); _scanTimer?.Dispose(); _scanTimer = null;
            _bScanStart.Enabled = true; _bScanStop.Enabled = false;
            Log("Sken zastaven. Unikatnich tagu: " + _scanTags.Count);
        }

        void ExportCsv()
        {
            if (_scanTags.Count == 0) { Log("Nic k exportu."); return; }
            using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "tag-scan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var sb = new StringBuilder("EPC;Cteni;RSSI_dBm;Antena;Kanal\r\n");
            foreach (var kv in _scanTags) sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0};{1};{2:0.0};{3};{4}", kv.Key, kv.Value.Count, kv.Value.Rssi, kv.Value.Antenna, kv.Value.Channel));
            File.WriteAllText(dlg.FileName, sb.ToString());
            Log("CSV ulozeno: " + dlg.FileName);
        }

        // ============================ TAB: PAMET ============================
        RichTextBox _memOut; TextBox _memPwd;
        TabPage TabMemory()
        {
            var tp = new TabPage("Pamet");
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(6, 8, 6, 6) };
            var bDump = Btn("Dump vsech bank", (s, e) => DumpMemory());
            var lbl = new Label { Text = "Access heslo (hex):", AutoSize = true, Padding = new Padding(10, 7, 2, 0) };
            _memPwd = new TextBox { Width = 90, Text = "00000000" };
            bar.Controls.AddRange(new Control[] { bDump, lbl, _memPwd });
            _memOut = OutBox();
            tp.Controls.Add(Group("Obsah pameti + zamky + vyklad EPC", _memOut)); tp.Controls.Add(bar);
            return tp;
        }

        async void DumpMemory()
        {
            if (!Ready()) return;
            var pwd = ParseHex(_memPwd.Text, 4); if (pwd == null) { Log("Neplatne heslo (4 B hex)."); return; }
            await RunBusy("Dump pameti", async () =>
            {
                var dumps = await Task.Run(() => _reader.FullMemoryDump(pwd, _currentEpc));
                var sb = new StringBuilder();
                sb.AppendLine("=== DUMP PAMETI " + DateTime.Now.ToString("HH:mm:ss") + " ===\n");
                foreach (var d in dumps)
                {
                    sb.AppendLine("[" + d.Bank + "]");
                    if (d.Ok && d.Data.Length > 0)
                    {
                        for (int w = 0; w * 2 < d.Data.Length; w++)
                        {
                            if (w % 8 == 0) sb.Append(string.Format("\n  w{0,-3} ", w));
                            sb.Append(string.Format("{0:X2}{1:X2} ", d.Data[w * 2], w * 2 + 1 < d.Data.Length ? d.Data[w * 2 + 1] : 0));
                        }
                        sb.AppendLine(); sb.AppendLine("  ASCII: " + TagDecode.AsciiPreview(d.Data));
                        if (d.Bank == Bank.Tid) { var t = TagDecode.DecodeTid(d.Data); sb.AppendLine("  >> " + t.Summary); if (t.Xtid) sb.AppendLine("  " + (TagDecode.ParseXtid(d.Data) ?? "")); }
                        if (d.Bank == Bank.Epc && d.Data.Length > 4) { var epc = d.Data.Skip(4).ToArray(); sb.AppendLine("  Vyklad: " + TagDecode.InterpretEpc(epc).Replace("\n", "\n  ")); }
                    }
                    else sb.AppendLine("  nelze precist: " + (d.Note ?? "?") + "  (banka zamcena / chybi / slaby signal)");
                    sb.AppendLine();
                }
                BeginInvoke((Action)(() => _memOut.Text = sb.ToString()));
            });
        }

        // ============================ TAB: TESTY ============================
        RichTextBox _testOut; NumericUpDown _soakDur, _powMin, _powMax;
        TabPage TabTests()
        {
            var tp = new TabPage("Testy");
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(4), FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            var bSens = Btn("Test citlivosti", (s, e) => TestSensitivity());
            var bChan = Btn("Kanalovy sken", (s, e) => TestChannels());
            var bSoak = Btn("Soak test", (s, e) => TestSoak());
            var bTemp = Btn("Teplota ctecky", (s, e) => TestTemp());
            _powMin = Spin("Pmin", 0, 33, 5); _powMax = Spin("Pmax", 0, 33, 20); _soakDur = Spin("Soak s", 1, 120, 10);
            bar.Controls.AddRange(new Control[] { bSens, bChan, bSoak, bTemp,
                Lab("Pmin:"), _powMin, Lab("Pmax:"), _powMax, Lab("Soak s:"), _soakDur });
            _testOut = OutBox();
            tp.Controls.Add(Group("Vysledky testu", _testOut)); tp.Controls.Add(bar);
            return tp;
        }

        Label Lab(string t) => new Label { Text = t, AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
        NumericUpDown Spin(string _, int min, int max, int val) => new NumericUpDown { Minimum = min, Maximum = max, Value = val, Width = 55 };

        async void TestSensitivity()
        {
            if (!Ready()) return;
            int pmin = (int)_powMin.Value, pmax = (int)_powMax.Value;
            await RunBusy("Test citlivosti", async () =>
            {
                AppendTest("=== TEST CITLIVOSTI (ramp vykonu) ===");
                var data = await Task.Run(() => _reader.SensitivityTest(_currentEpc, pmin, pmax, 0.4, s => AppendTest(s)));
                var first = data.FirstOrDefault(d => d.readRate > 0);
                if (first.power != 0 || first.readRate > 0) AppendTest(string.Format("\n>> Minimalni vykon pro cteni: {0} dBm", first.power));
                else AppendTest("\n>> Tag se nepodarilo precist v zadnem vykonu.");
            });
        }

        async void TestChannels()
        {
            if (!Ready()) return;
            await RunBusy("Kanalovy sken", async () =>
            {
                AppendTest("=== KANALOVY SKEN (2 s) ===");
                var stats = await Task.Run(() => _reader.ChannelScan(2.0, s => AppendTest(s)));
                foreach (var c in stats) AppendTest(string.Format("  kanal {0,2}: {1,4} cteni   avg RSSI {2:0.0} dBm", c.Channel, c.Count, c.AvgRssi));
                if (stats.Count == 0) AppendTest("  zadna data (tag v poli?)");
            });
        }

        async void TestSoak()
        {
            if (!Ready()) return;
            double sec = (double)_soakDur.Value;
            await RunBusy("Soak test", async () =>
            {
                AppendTest(string.Format("=== SOAK TEST ({0} s) ===", sec));
                var st = await Task.Run(() => _reader.SoakTest(sec, s => AppendTest(s)));
                AppendTest(string.Format("  Cteni/s      : {0:0.0}", st.ReadsPerSec));
                AppendTest(string.Format("  Celkem cteni : {0}   Unikatnich tagu: {1}", st.TotalReads, st.UniqueTags));
                AppendTest(string.Format("  RSSI min/avg/max: {0:0.0} / {1:0.0} / {2:0.0} dBm   odchylka {3:0.0}", st.RssiMin, st.RssiAvg, st.RssiMax, st.RssiStd));
                AppendTest("  >> Znamka stability: " + st.Grade);
            });
        }

        async void TestTemp()
        {
            if (!Ready()) return;
            await RunBusy("Teplota", async () =>
            {
                var c = await Task.Run(() => _reader.GetFullConfig());
                AppendTest(string.Format("=== TEPLOTA CTECKY ===\n  Aktualni: {0} C   Prah: {1} C", c.TempCur, c.TempLimit));
            });
        }

        void AppendTest(string s) { if (IsHandleCreated) BeginInvoke((Action)(() => _testOut.AppendText(s + "\r\n"))); }

        // ============================ TAB: KONFIGURACE ============================
        RichTextBox _cfgOut; ComboBox _cfgRegion; NumericUpDown _cfgPower, _cfgQ, _cfgSession, _cfgWorkmode;
        TabPage TabConfig()
        {
            var tp = new TabPage("Konfigurace");
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(4), WrapContents = true };
            var bGet = Btn("Nacti konfiguraci", (s, e) => CfgGet());
            var bSet = Btn("Uloz do ctecky", (s, e) => CfgSet());
            var bReboot = Btn("Reboot ctecky", (s, e) => CfgReboot());
            _cfgRegion = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
            _cfgRegion.Items.AddRange(new object[] { "1 CN 840-845", "2 CN 920-925", "3 EU 865-868", "4 US 902-928", "6 Korea", "7 Japonsko" });
            _cfgPower = Spin("", 0, 33, 17); _cfgQ = Spin("", 0, 15, 4); _cfgSession = Spin("", 0, 3, 0); _cfgWorkmode = Spin("", 0, 3, 0);
            bar.Controls.AddRange(new Control[] { bGet, bSet, bReboot, Lab("Region:"), _cfgRegion,
                Lab("Vykon:"), _cfgPower, Lab("Q:"), _cfgQ, Lab("Session:"), _cfgSession, Lab("WorkMode:"), _cfgWorkmode });
            _cfgOut = OutBox();
            tp.Controls.Add(Group("Parametry ctecky (snapshot)", _cfgOut)); tp.Controls.Add(bar);
            return tp;
        }

        async void CfgGet()
        {
            if (!Ready()) return;
            await RunBusy("Nacist konfiguraci", async () =>
            {
                var c = await Task.Run(() => _reader.GetFullConfig());
                var sb = new StringBuilder();
                sb.AppendLine("=== KONFIGURACE CTECKY ===");
                sb.AppendLine("Firmware : " + c.Firmware);
                sb.AppendLine("Hardware : " + c.Hardware);
                sb.AppendLine("SN       : " + c.Sn);
                sb.AppendLine(string.Format("Region   : {0} ({1})", c.Region, TagDecode.RegionName(c.Region)));
                sb.AppendLine("Vykon    : " + c.Power + " dBm");
                sb.AppendLine("Q / Session / WorkMode : " + c.QValue + " / " + c.Session + " / " + c.Workmode);
                sb.AppendLine("Antena   : " + c.Antenna);
                sb.AppendLine("Protokol : " + (c.RfidType == 0 ? "ISO 18000-6C (Gen2)" : "typ " + c.RfidType));
                sb.AppendLine("Teplota  : " + c.TempCur + " C (prah " + c.TempLimit + " C)");
                sb.AppendLine("Start freq (raw): " + c.FreqStart + "   step: " + c.FreqStep);
                BeginInvoke((Action)(() =>
                {
                    _cfgOut.Text = sb.ToString();
                    _cfgPower.Value = Clamp(c.Power, 0, 33); _cfgQ.Value = Clamp(c.QValue, 0, 15);
                    _cfgSession.Value = Clamp(c.Session, 0, 3); _cfgWorkmode.Value = Clamp(c.Workmode, 0, 3);
                    _cfgRegion.SelectedIndex = c.Region switch { 1 => 0, 2 => 1, 3 => 2, 4 => 3, 6 => 4, 7 => 5, _ => -1 };
                }));
            });
        }

        decimal Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));

        async void CfgSet()
        {
            if (!Ready()) return;
            if (_cfgRegion.SelectedIndex < 0) { Log("Vyber region."); return; }
            int region = new[] { 1, 2, 3, 4, 6, 7 }[_cfgRegion.SelectedIndex];
            int pow = (int)_cfgPower.Value, q = (int)_cfgQ.Value, ses = (int)_cfgSession.Value, wm = (int)_cfgWorkmode.Value;
            await RunBusy("Ulozit konfiguraci", async () =>
            {
                var r = await Task.Run(() => _reader.SetBasicConfig(region, pow, q, ses, wm));
                UiLog(r.Message);
            });
        }

        async void CfgReboot()
        {
            if (!Ready()) return;
            if (MessageBox.Show(this, "Opravdu rebootovat ctecku?", "Reboot", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await RunBusy("Reboot", async () => { var r = await Task.Run(() => _reader.Reboot()); UiLog(r.Message); });
        }

        // ============================ TAB: OPERACE ============================
        TextBox _opPwd, _opEpc, _opUserPtr, _opUserData, _opNewPwd, _opRecoveryList, _opMask;
        ComboBox _opLockArea, _opLockAction, _opPwdWhich; RichTextBox _opOut;
        Label _lblDict; readonly List<byte[]> _dictFile = new List<byte[]>(); volatile bool _recCancel;
        TabPage TabOps()
        {
            var tp = new TabPage("Operace") { BackColor = Theme.Bg };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 8 };

            var col = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(10, 8, 14, 8), BackColor = Theme.Bg };
            col.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            void AddCard(Card c) { c.Dock = DockStyle.Top; c.Margin = new Padding(2, 2, 2, 10); col.Controls.Add(c); col.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }

            // Pristup
            _opPwd = Input(110, "00000000");
            AddCard(OpCard("Pristup", Hint("Access heslo (4 B hex) se pouziva pro vsechny operace nize."),
                Row(Lab("Access heslo:"), _opPwd)));

            // Zapis
            _opEpc = Input(280);
            _opUserPtr = Input(44, "0");
            _opUserData = Input(240);
            AddCard(OpCard("Zapis dat",
                Row(Lab("Nove EPC (hex):"), _opEpc, Btn("Zapsat EPC", (s, e) => OpWriteEpc(), primary: true)),
                Row(Lab("USER od slova:"), _opUserPtr, Lab("data (hex):"), _opUserData, Btn("Zapsat USER", (s, e) => OpWriteUser()))));

            // Hesla a zamky
            _opPwdWhich = Combo(90, "access", "kill");
            _opNewPwd = Input(100);
            _opLockArea = Combo(120, "Kill heslo", "Access heslo", "EPC", "TID", "USER"); _opLockArea.SelectedIndex = 2;
            _opLockAction = Combo(180, "0 odemceno (r/w)", "1 trvale odemceno", "2 zamceno heslem", "3 PERMALOCK"); _opLockAction.SelectedIndex = 2;
            AddCard(OpCard("Hesla a zamky",
                Row(Lab("Nastavit heslo:"), _opPwdWhich, Lab("nove (hex):"), _opNewPwd, Btn("Nastavit", (s, e) => OpSetPwd())),
                Row(Lab("Lock oblast:"), _opLockArea, Lab("akce:"), _opLockAction, Btn("Zamknout", (s, e) => OpLock()))));

            // Obnova hesla
            _opRecoveryList = Input(300);
            _opMask = Input(110, "XXXXXXXX");
            _lblDict = new Label { Text = "slovnik nenacten", AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(6, 6, 0, 0) };
            AddCard(OpCard("Obnova zapomenuteho access hesla",
                Hint("Zkusi tvoje kandidaty + nahrany slovnik + chytry slovnik (default hesla, odvozena z EPC/TID). Funguje jen na zamcenem tagu, nedestruktivne."),
                Row(Lab("Vlastni kandidati (hex, carkami):"), _opRecoveryList),
                Row(Btn("Nahrat slovnik ze souboru...", (s, e) => OpLoadDict()), _lblDict),
                Row(Lab("Maska (8 hex, X = nezname):"), _opMask, Btn("Generovat a zkusit", (s, e) => OpRecoverMask())),
                Row(Btn("Spustit obnovu (slovniky)", (s, e) => OpRecover(), primary: true), Btn("Zastavit", (s, e) => _recCancel = true, danger: true))));

            // Likvidace
            AddCard(OpCard("Likvidace tagu",
                Hint("KILL je NEVRATNY. Pouzije KILL heslo z pole 'nove (hex)' v sekci Hesla, jinak Access heslo."),
                Row(Btn("KILL TAG", (s, e) => OpKill(), danger: true))));

            _opOut = OutBox();
            split.Panel1.Controls.Add(col);
            split.Panel2.Controls.Add(Group("Vystup operaci", _opOut));
            tp.Controls.Add(split);
            tp.HandleCreated += (s, e) => { try { split.SplitterDistance = (int)(split.Width * 0.60); } catch { } };
            return tp;
        }

        // karta jedne sekce operaci
        Card OpCard(string title, params Control[] rows)
        {
            var card = new Card(title) { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(560, 0), Margin = new Padding(2, 2, 2, 8) };
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top, BackColor = Color.Transparent };
            foreach (var r in rows) stack.Controls.Add(r);
            card.Controls.Add(stack);
            return card;
        }

        FlowLayoutPanel Row(params Control[] c)
        {
            var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 2), BackColor = Color.Transparent };
            p.Controls.AddRange(c);
            return p;
        }

        Label Hint(string t) => new Label { Text = t, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(2, 0, 0, 4), MaximumSize = new Size(540, 0) };

        TextBox Input(int w, string text = "") => new TextBox { Width = w, Text = text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 3, 4, 3) };

        ComboBox Combo(int w, params string[] items)
        {
            var c = new ComboBox { Width = w, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Margin = new Padding(4, 3, 4, 3) };
            c.Items.AddRange(items); if (items.Length > 0) c.SelectedIndex = 0;
            return c;
        }

        byte[] Pwd() => ParseHex(_opPwd.Text, 4);

        async void OpWriteEpc()
        {
            if (!Ready()) return;
            var epc = ParseHex(_opEpc.Text, 0); var pwd = Pwd();
            if (epc == null || epc.Length == 0 || epc.Length % 2 != 0) { OpLog("Neplatne EPC (hex, celá slova)."); return; }
            if (pwd == null) { OpLog("Neplatne access heslo."); return; }
            if (MessageBox.Show(this, "Prepsat EPC na " + TagDecode.Hex(epc, false) + "?", "Zapis EPC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await RunBusy("Zapis EPC", async () => { if (_currentEpc != null) _reader.SelectByEpc(_currentEpc); var r = await Task.Run(() => _reader.WriteEpc(epc, pwd)); OpLog(r.Message); });
        }

        async void OpWriteUser()
        {
            if (!Ready()) return;
            var data = ParseHex(_opUserData.Text, 0); var pwd = Pwd();
            if (data == null || data.Length == 0 || data.Length % 2 != 0) { OpLog("Neplatna USER data (hex, celá slova)."); return; }
            if (pwd == null) { OpLog("Neplatne heslo."); return; }
            if (!ushort.TryParse(_opUserPtr.Text, out var ptr)) { OpLog("Neplatne 'od slova'."); return; }
            await RunBusy("Zapis USER", async () => { if (_currentEpc != null) _reader.SelectByEpc(_currentEpc); var r = await Task.Run(() => _reader.WriteMemory(Bank.User, ptr, data, pwd)); OpLog(r.Message); });
        }

        async void OpSetPwd()
        {
            if (!Ready()) return;
            var np = ParseHex(_opNewPwd.Text, 4); var pwd = Pwd();
            if (np == null) { OpLog("Nove heslo musi byt 4 B hex."); return; }
            if (pwd == null) { OpLog("Neplatne access heslo."); return; }
            bool kill = _opPwdWhich.SelectedIndex == 1;
            await RunBusy("Nastavit heslo", async () => { if (_currentEpc != null) _reader.SelectByEpc(_currentEpc); var r = await Task.Run(() => _reader.SetPassword(kill, np, pwd)); OpLog(r.Message); });
        }

        async void OpLock()
        {
            if (!Ready()) return;
            var pwd = Pwd(); if (pwd == null) { OpLog("Neplatne access heslo."); return; }
            var area = (LockArea)(new[] { 0, 1, 2, 3, 4 }[_opLockArea.SelectedIndex]);
            var action = (LockAction)_opLockAction.SelectedIndex;
            if (action == LockAction.Permalock && MessageBox.Show(this, "PERMALOCK je NEVRATNY. Pokracovat?", "Permalock", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await RunBusy("Lock", async () => { if (_currentEpc != null) _reader.SelectByEpc(_currentEpc); var r = await Task.Run(() => _reader.LockBank(area, action, pwd)); OpLog(r.Message); });
        }

        async void OpKill()
        {
            if (!Ready()) return;
            var kp = ParseHex(_opNewPwd.Text, 4) ?? Pwd();
            if (kp == null) { OpLog("Zadej KILL heslo do pole 'nove (hex)' nebo 'access heslo'."); return; }
            if (MessageBox.Show(this, "KILL tag je NEVRATNE znici tag!\nHeslo: " + TagDecode.Hex(kp, false) + "\nPokracovat?", "KILL TAG", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await RunBusy("Kill", async () => { var r = await Task.Run(() => _reader.Kill(kp, _currentEpc)); OpLog(r.Message); });
        }

        void OpLoadDict()
        {
            using var dlg = new OpenFileDialog { Filter = "Slovnik hesel (*.txt;*.dic;*.lst)|*.txt;*.dic;*.lst|Vse (*.*)|*.*" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var parsed = CF601Reader.ParseDictionaryText(File.ReadAllText(dlg.FileName));
                _dictFile.Clear(); _dictFile.AddRange(parsed);
                _lblDict.Text = string.Format("nacteno {0} hesel ({1})", _dictFile.Count, Path.GetFileName(dlg.FileName));
                _lblDict.ForeColor = _dictFile.Count > 0 ? Theme.Green : Theme.Bad;
                OpLog(string.Format("Slovnik nacten: {0} hesel ze souboru {1}", _dictFile.Count, dlg.FileName));
            }
            catch (Exception ex) { OpLog("CHYBA cteni slovniku: " + ex.Message); }
        }

        async void OpRecover()
        {
            if (!Ready()) return;
            if (_currentEpc == null) { OpLog("Nejprve analyzuj tag (zalozka Analyza), aby byl vybran."); return; }
            var extra = new List<byte[]>();
            foreach (var part in _opRecoveryList.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            { var b = ParseHex(part, 4); if (b != null) extra.Add(b); }
            byte[] tid = _last?.Tid?.RawBytes;
            _recCancel = false;
            await RunBusy("Obnova hesla", async () =>
            {
                OpLog("=== OBNOVA ACCESS HESLA (slovniky) ===");
                var dict = CF601Reader.PasswordDictionary(_currentEpc, tid);
                var all = extra.Concat(_dictFile).Concat(dict).ToList();
                OpLog(string.Format("Kandidatu: {0} (vlastni {1} + soubor {2} + chytry {3})", all.Count, extra.Count, _dictFile.Count, dict.Count));
                var found = await Task.Run(() => _reader.RecoverAccessPassword(all, _currentEpc, s => OpLog(s), () => _recCancel, all.Count));
                OpLog(found != null ? ">> HESLO: " + TagDecode.Hex(found, false) : ">> Heslo nenalezeno.");
            });
        }

        async void OpRecoverMask()
        {
            if (!Ready()) return;
            if (_currentEpc == null) { OpLog("Nejprve analyzuj tag (zalozka Analyza), aby byl vybran."); return; }
            string mask = (_opMask.Text ?? "").Trim();
            long count = CF601Reader.MaskCount(mask);
            if (count < 0) { OpLog("Neplatna maska. Zadej 8 znaku: hex cislice nebo X (napr. 1234XXXX)."); return; }
            if (count > 1_000_000 &&
                MessageBox.Show(this, string.Format("Maska ma {0:N0} kombinaci – muze to trvat velmi dlouho (hodiny az dny).\nPokracovat?", count),
                    "Velky rozsah", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _recCancel = false;
            await RunBusy("Obnova hesla (maska)", async () =>
            {
                OpLog(string.Format("=== OBNOVA PODLE MASKY {0} ({1:N0} kombinaci) ===", mask.ToUpperInvariant(), count));
                var found = await Task.Run(() => _reader.RecoverAccessPassword(
                    CF601Reader.GenerateFromMask(mask), _currentEpc, s => OpLog(s), () => _recCancel, count));
                OpLog(found != null ? ">> HESLO: " + TagDecode.Hex(found, false) : ">> Heslo nenalezeno v masce.");
            });
        }

        void OpLog(string s) { if (IsHandleCreated) BeginInvoke((Action)(() => _opOut.AppendText(s + "\r\n"))); Log(s); }

        // ============================ SPOLECNE ============================
        void Connect()
        {
            try
            {
                int n = CF601Reader.UsbCount();
                Log("USB ctecek: " + n);
                if (n <= 0) { MessageBox.Show(this, "CF601 nenalezena na USB.", "Ctecka", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                try { Log("USB info: " + CF601Reader.UsbInfo(0)); } catch { }
                _reader = new CF601Reader();
                _reader.Open(0);
                var di = _reader.GetDeviceInfo();
                _lblDevice.Text = string.Format("FW {0}  HW {1}  SN {2}  |  {3}  {4} dBm", SafeS(di.Firmware), SafeS(di.Hardware), SafeS(di.Sn), SafeS(di.RegionName), di.Power);
                if (di.Workmode != 0) Log("POZN.: WORKMODE=" + di.Workmode + " – pro cteni pameti pouzij WORKMODE=0 (zalozka Konfigurace).");
                SetConnected(true);
                Log("Ctecka pripojena (HID).");
            }
            catch (Exception ex) { _reader?.Dispose(); _reader = null; SetConnected(false); Log("CHYBA: " + ex.Message); MessageBox.Show(this, ex.Message, "Chyba pripojeni", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void Disconnect()
        {
            ScanStop();
            try { _reader?.Dispose(); } catch { }
            _reader = null; SetConnected(false); _lblDevice.Text = ""; _lblTag.Text = "Tag: -"; _currentEpc = null;
            Log("Odpojeno.");
        }

        bool Ready()
        {
            if (_reader == null || !_reader.IsOpen) { Log("Nejprve pripoj ctecku."); return false; }
            if (_busy) { Log("Probiha jina operace, pockej."); return false; }
            return true;
        }

        async Task RunBusy(string name, Func<Task> work)
        {
            _busy = true; _tabs.Enabled = true;
            try { await work(); }
            catch (Exception ex) { Log("CHYBA (" + name + "): " + ex.Message); }
            finally { _busy = false; }
        }

        void SetConnected(bool on)
        {
            _btnConnect.Enabled = !on; _btnDisconnect.Enabled = on;
            if (_led != null) _led.On = on;
            _lblState.Text = on ? "Pripojeno" : "Odpojeno";
            _lblState.ForeColor = on ? Theme.Green : Theme.Bad;
        }

        static string SafeS(string s) => string.IsNullOrEmpty(s) ? "?" : s;

        // hex parser: expectLen>0 vynuti presnou delku v bajtech; 0 = libovolna
        static byte[] ParseHex(string s, int expectLen)
        {
            if (s == null) return null;
            var clean = new string(s.Where(Uri.IsHexDigit).ToArray());
            if (clean.Length % 2 != 0) return null;
            var b = new byte[clean.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
            if (expectLen > 0 && b.Length != expectLen) return null;
            return b;
        }

        void UiLog(string s) { if (IsHandleCreated) BeginInvoke((Action)(() => Log(s))); }

        void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg + Environment.NewLine;
            if (_log.IsHandleCreated && _log.InvokeRequired) _log.BeginInvoke((Action)(() => _log.AppendText(line)));
            else _log.AppendText(line);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ScanStop();
            try { _reader?.Dispose(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
