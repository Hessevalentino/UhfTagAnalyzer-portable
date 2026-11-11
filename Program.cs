using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UhfTagAnalyzer
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Nativni DLL (UHFPrimeReader/hidapi) nacitej z adresare programu – robustne,
            // nezavisle na aktualni pracovni slozce i pri single-file exe.
            try
            {
                string dir = AppContext.BaseDirectory;
                NativeLibrary.SetDllImportResolver(typeof(NativeApi).Assembly, (name, asm, search) =>
                {
                    string candidate = Path.Combine(dir, name);
                    if (!Path.HasExtension(candidate)) candidate += ".dll";
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var h)) return h;
                    return IntPtr.Zero; // necha default hledani
                });
            }
            catch { }

            if (args != null && Array.IndexOf(args, "--selftest-decode") >= 0) { Environment.Exit(SelfTestDecode()); return; }
            if (args != null && Array.IndexOf(args, "--selftest") >= 0) { SelfTest(); return; }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new MainForm();
            int si = args == null ? -1 : Array.IndexOf(args, "--screenshots");
            if (si >= 0 && si + 1 < args.Length)
            {
                string dir = args[si + 1];
                form.Shown += async (s, e) => await form.ScreenshotTour(dir);
            }
            Application.Run(form);
        }

        // Offline testy dekoderu (bez ctecky). Spusteni: UhfTagAnalyzer.exe --selftest-decode
        // Vystup: selftest-decode.log vedle exe; navratovy kod = pocet selhanych kontrol.
        // [HW] = hodnota overena na realnem tagu (Impinj M750), [DS] = dle datasheetu vyrobce,
        // [REG] = regresni (rucne prepocitano).
        static int SelfTestDecode()
        {
            var sb = new System.Text.StringBuilder();
            int fail = 0, total = 0;
            void Check(string name, bool ok, string detail = "")
            {
                total++; if (!ok) fail++;
                sb.AppendLine((ok ? "[OK]   " : "[FAIL] ") + name + (detail.Length > 0 ? "   (" + detail + ")" : ""));
            }
            byte[] H(string hex) => Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();

            var epc = H("300ED89F3350016A1E641753");
            var tid = H("E280119020006A5A2F720353");

            // CRC-16 nad PC+EPC
            var crc = TagDecode.Gen2Crc16(H("3400"), epc);
            Check("[HW]  Gen2 CRC-16 (PC 3400 + EPC) = B6A1", TagDecode.SameBytes(crc, H("B6A1")), TagDecode.Hex(crc, false));

            // PC slovo
            var pc = TagDecode.DecodePc(H("3400"));
            Check("[HW]  PC 3400 -> 6 slov EPC, UMI=1, XPC=0, GS1", pc.EpcWords == 6 && pc.Umi && !pc.Xpc && pc.Toggle == 0);

            // TID
            var t = TagDecode.DecodeTid(tid);
            Check("[HW]  TID -> Impinj M750 (MDID 0x001, TMN 0x190; EPC 96 + USER 32 sedi s tagem)",
                t.Vendor == "Impinj" && t.Model == "M750" && t.Mdid == "0x001" && t.Tmn == "0x190", t.Summary);
            Check("[DS]  M750 ma poznamku o sdilenem hesle", t.ModelNote != null && t.ModelNote.Contains("sdilene"));
            Check("[HW]  TID -> XTID + serializovane", t.Xtid && t.Serialized);
            var x = TagDecode.ParseXtid(tid) ?? "";
            Check("[HW]  XTID 0x2000 -> 48bit serial 6A5A2F720353", x.Contains("0x2000") && x.Contains("48 bit") && x.Contains("6A5A2F720353"), x.Replace("\n", " | "));
            var t2 = TagDecode.DecodeTid(H("E2801160"));   // Monza R6, jen prvni 2 slova
            Check("[DS]  E2801160 -> Impinj Monza R6 (+ poznamka: hesla nema)", t2.Model == "Monza R6" && t2.ModelNote != null, t2.Summary);
            Check("[DS]  E2801191 -> Impinj M730", TagDecode.DecodeTid(H("E2801191")).Model == "M730");
            Check("[DS]  E2801170 -> Impinj Monza R6-P", TagDecode.DecodeTid(H("E2801170")).Model == "Monza R6-P");
            Check("[DS]  E2801105 -> Impinj Monza 4QT", TagDecode.DecodeTid(H("E2801105")).Model == "Monza 4QT");
            var t3 = TagDecode.DecodeTid(H("E2806894"));
            Check("[DS]  E2806894 -> NXP UCODE 8", t3.Vendor == "NXP Semiconductors" && t3.Model == "UCODE 8", t3.Summary);
            Check("[DS]  E2806995 -> NXP UCODE 9", TagDecode.DecodeTid(H("E2806995")).Model == "UCODE 9");
            var t4 = TagDecode.DecodeTid(H("E2003412"));
            Check("[DS]  E2003412 -> Alien Higgs-3, bez XTID", t4.Vendor == "Alien Technology" && t4.Model == "Higgs-3" && !t4.Xtid, t4.Summary);
            Check("[REG] MDID 0x008 = EP Microelectronics (ne EM)", TagDecode.DecodeTid(H("E2808000")).Vendor == "EP Microelectronics");
            Check("[REG] MDID 0x00B = EM Microelectronic", TagDecode.DecodeTid(H("E280B000")).Vendor == "EM Microelectronic");
            var t5 = TagDecode.DecodeTid(H("E2801FFF"));
            Check("[REG] neznamy TMN -> vyrobce + 'neni v tabulce'", t5.Model == null && t5.Summary.Contains("neni v tabulce"), t5.Summary);

            // EPC – SGTIN-96
            var e = TagDecode.InterpretEpc(epc);
            Check("[REG] SGTIN-96: GTIN-14 07640154130050, serial 180898502483",
                e.Contains("SGTIN-96") && e.Contains("07640154130050") && e.Contains("180898502483"), e.Replace("\n", " | "));
            Check("[REG] EPC header 0x36 = SGTIN-198", TagDecode.InterpretEpc(H("36000000000000000000000000")).Contains("SGTIN-198"));
            Check("[REG] EPC header 0x2F = USDoD-96", TagDecode.InterpretEpc(H("2F0000000000000000000000")).Contains("USDoD-96"));
            Check("[REG] neznamy header nespadne", TagDecode.InterpretEpc(H("E20000000000")).Contains("neni standardni"));

            // Hesla – maska a slovnik
            Check("[REG] maska 1234XXXX = 65536 kombinaci", CF601Reader.MaskCount("1234XXXX") == 65536);
            Check("[REG] maska s neplatnym znakem = -1", CF601Reader.MaskCount("1234XXXZ") == -1);
            var m = CF601Reader.GenerateFromMask("123456XX").ToList();
            Check("[REG] maska 123456XX: 256 kandidatu, prvni 12345600, posledni 123456FF",
                m.Count == 256 && TagDecode.Hex(m[0], false) == "12345600" && TagDecode.Hex(m[255], false) == "123456FF");
            var d = CF601Reader.ParseDictionaryText("12345678, aabbccdd\nxyz 0x00000001\n123");
            Check("[REG] parser slovniku: 3 platna hesla", d.Count == 3, d.Count + " hesel");
            Check("[REG] chytry slovnik bez duplicit", CF601Reader.PasswordDictionary(epc, tid).Select(b => TagDecode.Hex(b, false)).Distinct().Count()
                                                       == CF601Reader.PasswordDictionary(epc, tid).Count);

            // Regiony (kodovani frekvence jako v SetRegion: cele MHz + kHz)
            foreach (var (code, mhz, khz) in new[] { (6, 865, 700), (3, 865, 100), (1, 902, 750), (7, 840, 125) })
            {
                var ri = TagDecode.Region(code);
                int fm = (int)Math.Floor(ri.StartMHz + 1e-9), fk = (int)Math.Round((ri.StartMHz - fm) * 1000);
                Check(string.Format("[REG] region {0}: start {1}.{2:D3} MHz", code, mhz, khz), fm == mhz && fk == khz, ri.ToString());
            }
            Check("[HW]  REGION=1 + 902.750/500/50 = USA (overeno na CF601)", TagDecode.Region(1).Name.StartsWith("USA"));
            Check("[REG] EU regiony = 3 a 6", TagDecode.IsEuRegion(3) && TagDecode.IsEuRegion(6) && !TagDecode.IsEuRegion(1));

            // Chybove kody
            Check("[REG] -250 ma popis", !ErrorCodes.Text(-250).StartsWith("Neznama"));
            Check("[REG] -244 = chybne heslo", ErrorCodes.Text(-244).Contains("heslo"));

            sb.Insert(0, string.Format("=== SELFTEST DEKODERU {0}: {1}/{2} OK ===\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), total - fail, total));
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest-decode.log"), sb.ToString()); } catch { }
            return fail;
        }

        // Headless diagnostika skutecneho kodu aplikace proti pripojene ctecce – JEN CTENI
        // (do tagu nic nezapisuje). Spusteni: UhfTagAnalyzer.exe --selftest
        // Vystup: selftest.log vedle exe (GUI exe nema konzoli).
        static void SelfTest()
        {
            var sb = new System.Text.StringBuilder();
            string logPath = Path.Combine(AppContext.BaseDirectory, "selftest.log");
            void W(string s) { sb.AppendLine(s); try { File.WriteAllText(logPath, sb.ToString()); } catch { } }
            W("=== UhfTagAnalyzer SELFTEST (jen cteni) " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            W("USB ctecek: " + CF601Reader.UsbCount());   // NUTNE pred otevrenim (inicializace HID v DLL)
            using var r = new CF601Reader();
            try
            {
                r.OpenAndPrepare(0, s => W("  " + s));

                // volitelne: --set-region <kod>  (napr. 6 = EU/ETSI 4 kanaly)
                var argv = Environment.GetCommandLineArgs();
                int ai = Array.IndexOf(argv, "--set-region");
                if (ai >= 0 && ai + 1 < argv.Length && int.TryParse(argv[ai + 1], out int code))
                {
                    var before = r.GetFullConfig();
                    W(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Region pred: {0} = {1}  ({2:0.000}–{3:0.000} MHz)", before.Region, TagDecode.RegionName(before.Region), before.FreqStartMHz, before.FreqEndMHz));
                    var sr = r.SetRegion(code);
                    W("SetRegion(" + code + "): " + (sr.Ok ? "OK – " : "CHYBA – ") + sr.Message);
                }

                // volitelne: --set-power <dBm>
                int pi = Array.IndexOf(argv, "--set-power");
                if (pi >= 0 && pi + 1 < argv.Length && int.TryParse(argv[pi + 1], out int pw))
                {
                    var c = r.GetFullConfig();
                    var sp = r.SetBasicConfig(pw, c.QValue, c.Session, c.Workmode);
                    W("Vykon " + c.Power + " -> " + pw + " dBm: " + sp.Message + " (overeni: " + r.GetFullConfig().Power + " dBm)");
                }

                var di = r.GetDeviceInfo();
                W(string.Format("FW {0}  HW {1}  SN {2}  region {3}  {4} dBm  WORKMODE {5}",
                    di.Firmware, di.Hardware, di.Sn, di.RegionName, di.Power, di.Workmode));
                W("Spoustim Analyze(1.0s)...");
                var res = r.Analyze(1.0, 6, 64, s => W("  " + s));
                W("");
                W(res.Report ?? "(zadny report)");

                void Rd(Bank b, ushort ptr, byte cnt)
                {
                    var br = r.ReadMemory(b, ptr, cnt);
                    W(string.Format("  {0,-8} ptr={1} cnt={2} -> {3}  data={4}  [tag EPC={5} PC={6} CRC={7}]", b, ptr, cnt,
                        br.Ok ? "OK" : br.Status + " " + br.StatusText,
                        br.Ok ? TagDecode.Hex(br.Data, false) : "-",
                        br.Epc != null ? TagDecode.Hex(br.Epc, false) : "-",
                        br.Pc != null ? TagDecode.Hex(br.Pc, false) : "-",
                        br.Crc != null ? TagDecode.Hex(br.Crc, false) : "-"));
                }

                if (Array.IndexOf(argv, "--sens") >= 0 && res.Tag != null)
                {
                    W("\n=== TEST CITLIVOSTI (ramp vykonu 17..0 dBm, vykon se pak vrati) ===");
                    var swS = System.Diagnostics.Stopwatch.StartNew();
                    var data = r.SensitivityTest(res.Tag.Epc, 0, 17, 0.5, s => W("  " + s));
                    var ok = data.Where(d => d.readRate > 0).OrderBy(d => d.power).ToList();
                    W(ok.Count > 0 ? "  >> Minimalni vykon pro cteni: " + ok[0].power + " dBm" : "  >> Tag se nepodarilo precist v zadnem vykonu");
                    W("  (test trval " + swS.Elapsed.TotalSeconds.ToString("0") + " s)");
                    W("  Vykon po testu: " + r.GetFullConfig().Power + " dBm");
                }

                W("\n=== Dump pameti (se selectem na tag) ===");
                var swd = System.Diagnostics.Stopwatch.StartNew();
                foreach (var d in r.FullMemoryDump(new byte[4], res.Tag?.Epc))
                    W(string.Format("  {0,-8} {1}  {2} B  {3}", d.Bank, d.Ok ? "OK " : "-- ", d.Data.Length,
                        d.Ok ? TagDecode.Hex(d.Data, false) : d.Note));
                W("  (trvalo " + swd.ElapsedMilliseconds + " ms)");

                if (res.Tag != null)
                {
                    W("\n=== VYBER podle spravneho EPC (TID ma projit) ===");
                    W("  SetSelectMask -> " + r.SelectByEpc(res.Tag.Epc));
                    Rd(Bank.Tid, 0, 6);
                    var bad = (byte[])res.Tag.Epc.Clone(); bad[bad.Length - 1] ^= 0xFF;
                    W("=== VYBER podle CHYBNEHO EPC (TID ma selhat) ===");
                    W("  SetSelectMask -> " + r.SelectByEpc(bad));
                    Rd(Bank.Tid, 0, 6);
                    r.ClearSelect();
                }
                W("\n=== HOTOVO ===");
            }
            catch (Exception ex) { W("CHYBA: " + ex); }
        }
    }
}
