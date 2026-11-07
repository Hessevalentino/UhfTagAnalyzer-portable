using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace UhfTagAnalyzer
{
    // Vysledky testu a konfigurace
    public class OpResult
    {
        public bool Ok;
        public int Status;
        public string Message;
        public string StatusText => ErrorCodes.Text(Status);
    }

    public class ReaderConfig
    {
        public int Region, Power, QValue, Session, Workmode, Antenna, RfidType;
        public int FreqStart, FreqStop, FreqStep; // kHz-ish dle FreqInfo
        public int TempCur, TempLimit;
        public string Firmware, Hardware, Sn;
        public DevicePara Raw;
    }

    public class ChannelStat { public int Channel; public int Count; public double AvgRssi; }

    public class SoakStat
    {
        public int TotalReads, UniqueTags, Polls;
        public double Seconds, ReadsPerSec, RssiMin, RssiMax, RssiAvg, RssiStd;
        public string Grade;
    }

    public partial class CF601Reader
    {
        // ---------------- ZAPIS / OPERACE ----------------

        public OpResult WriteMemory(Bank bank, ushort wordPtr, byte[] data, byte[] accPwd = null)
        {
            accPwd ??= new byte[4];
            if (data == null || data.Length == 0 || data.Length % 2 != 0)
                return new OpResult { Ok = false, Message = "Data musi byt po celych slovech (2 B)." };
            byte words = (byte)(data.Length / 2);
            int res = NativeApi.WriteTag(_h, 0, accPwd, (byte)bank, wordPtr, words, data);
            return new OpResult { Ok = res == 0, Status = res, Message = res == 0 ? "Zapis OK" : ErrorCodes.Text(res) };
        }

        // Zapis noveho EPC (prepocita PC slovo z delky). newEpc = cele EPC v bajtech (nasobek 2).
        public OpResult WriteEpc(byte[] newEpc, byte[] accPwd = null)
        {
            if (newEpc == null || newEpc.Length == 0 || newEpc.Length % 2 != 0)
                return new OpResult { Ok = false, Message = "EPC musi byt v celych slovech (2 B)." };
            int words = newEpc.Length / 2;
            if (words > 31) return new OpResult { Ok = false, Message = "EPC je prilis dlouhe (max 31 slov)." };
            int pc = (words << 11) & 0xF800; // horni 5 bitu = delka v slovech
            var buf = new byte[2 + newEpc.Length];
            buf[0] = (byte)(pc >> 8); buf[1] = (byte)(pc & 0xFF);
            Array.Copy(newEpc, 0, buf, 2, newEpc.Length);
            // zapis od slova 1 (PC), nasleduje EPC
            return WriteMemory(Bank.Epc, 1, buf, accPwd);
        }

        public OpResult LockBank(LockArea area, LockAction action, byte[] accPwd = null)
        {
            accPwd ??= new byte[4];
            int res = NativeApi.LockTag(_h, accPwd, (byte)area, (byte)action);
            return new OpResult { Ok = res == 0, Status = res, Message = res == 0 ? "Lock OK" : ErrorCodes.Text(res) };
        }

        // Nastaveni hesla: which=kill -> RESERVED word 0..1; which=access -> word 2..3
        public OpResult SetPassword(bool killPwd, byte[] newPwd, byte[] accPwd = null)
        {
            if (newPwd == null || newPwd.Length != 4)
                return new OpResult { Ok = false, Message = "Heslo musi mit 4 bajty." };
            ushort ptr = (ushort)(killPwd ? 0 : 2);
            return WriteMemory(Bank.Reserved, ptr, newPwd, accPwd);
        }

        public OpResult Kill(byte[] killPwd, byte[] selectEpc = null)
        {
            if (killPwd == null || killPwd.Length != 4)
                return new OpResult { Ok = false, Message = "Kill heslo musi mit 4 bajty." };
            if (killPwd.All(b => b == 0))
                return new OpResult { Ok = false, Message = "Kill s nulovym heslem neni povolen (tag se nezabije)." };
            if (selectEpc != null) SelectByEpc(selectEpc);
            int res = NativeApi.KillTag(_h, killPwd);
            return new OpResult { Ok = res == 0, Status = res, Message = res == 0 ? "Tag zabit (kill OK)" : ErrorCodes.Text(res) };
        }

        // ---------------- OBNOVA HESLA (vlastni tagy) ----------------
        // Oracle: zkusi zapsat zpet STEJNA data do EPC banky. Funguje jen kdyz je
        // EPC banka zamcena (jinak projde i spatne heslo). Vraci nalezene heslo nebo null.

        public bool IsEpcBankLocked(out byte[] currentPcEpc)
        {
            currentPcEpc = null;
            var r = ReadMemory(Bank.Epc, 1, 7); // PC + az 6 slov EPC
            if (!r.Ok || r.Data.Length < 2) return false;
            // delka EPC z PC
            int pc = (r.Data[0] << 8) | r.Data[1];
            int words = (pc >> 11) & 0x1F;
            int need = 2 + words * 2;
            if (r.Data.Length < need) { currentPcEpc = r.Data; }
            else currentPcEpc = r.Data.Take(need).ToArray();
            // zkus zapis se spatnym (nulovym) heslem; pokud selze na zamku -> je zamceno
            var probe = WriteMemory(Bank.Epc, 1, currentPcEpc, new byte[] { 0, 0, 0, 0 });
            if (probe.Ok) return false; // zapis prosel -> neni zamceno
            // zamceno / chyba hesla
            return probe.Status == -189 || probe.Status == -172 || probe.Status == -188
                || probe.Status == -187 || probe.Status == -191 || probe.Status == -174;
        }

        public byte[] RecoverAccessPassword(IEnumerable<byte[]> candidates, byte[] selectEpc,
                                            Action<string> progress, Func<bool> cancel = null, long total = 0)
        {
            if (selectEpc != null) SelectByEpc(selectEpc);
            if (!IsEpcBankLocked(out var pcEpc) || pcEpc == null)
            {
                progress?.Invoke("EPC banka neni zamcena – oracle nefunguje, obnovu nelze provest.");
                return null;
            }
            long tried = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var cand in candidates)
            {
                if (cancel != null && cancel()) { progress?.Invoke(string.Format("Preruseno po {0} pokusech.", tried)); return null; }
                if (cand == null || cand.Length != 4) continue;
                tried++;
                if (selectEpc != null) SelectByEpc(selectEpc);
                var w = WriteMemory(Bank.Epc, 1, pcEpc, cand);
                if (w.Ok)
                {
                    progress?.Invoke(string.Format("NALEZENO po {0} pokusech: {1}", tried, TagDecode.Hex(cand, false)));
                    return cand;
                }
                if (tried % 100 == 0)
                {
                    double rate = tried / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
                    string eta = "";
                    if (total > 0 && rate > 0)
                    {
                        double sec = (total - tried) / rate;
                        eta = "  zbyva ~" + FmtDur(sec);
                    }
                    progress?.Invoke(string.Format("{0}{1} pokusu   {2:0} /s{3}",
                        tried, total > 0 ? "/" + total : "", rate, eta));
                }
            }
            progress?.Invoke(string.Format("Heslo nenalezeno ({0} kandidatu).", tried));
            return null;
        }

        static string FmtDur(double s)
        {
            if (s < 90) return s.ToString("0") + " s";
            if (s < 5400) return (s / 60).ToString("0") + " min";
            if (s < 172800) return (s / 3600).ToString("0.0") + " h";
            return (s / 86400).ToString("0.0") + " dni";
        }

        // Generator kandidatu z masky: 8 znaku, hex cislice = pevne, X/x/* = neznama nibble.
        // Priklad "1234XXXX" -> 65536 kombinaci. Lazy (negeneruje do pameti najednou).
        public static IEnumerable<byte[]> GenerateFromMask(string mask)
        {
            mask = (mask ?? "").Trim();
            var slots = new int[8];        // -1 = wildcard, jinak hodnota nibble
            var wild = new System.Collections.Generic.List<int>();
            if (mask.Length != 8) yield break;
            for (int i = 0; i < 8; i++)
            {
                char c = mask[i];
                if (c == 'X' || c == 'x' || c == '*' || c == '?') { slots[i] = -1; wild.Add(i); }
                else if (Uri.IsHexDigit(c)) slots[i] = Convert.ToInt32(c.ToString(), 16);
                else yield break; // neplatny znak
            }
            int n = wild.Count;
            long count = 1L << (4 * n);     // 16^n
            var cur = (int[])slots.Clone();
            for (long k = 0; k < count; k++)
            {
                long kk = k;
                for (int w = 0; w < n; w++) { cur[wild[w]] = (int)(kk & 0xF); kk >>= 4; }
                yield return new[]
                {
                    (byte)((cur[0] << 4) | cur[1]),
                    (byte)((cur[2] << 4) | cur[3]),
                    (byte)((cur[4] << 4) | cur[5]),
                    (byte)((cur[6] << 4) | cur[7]),
                };
            }
        }

        public static long MaskCount(string mask)
        {
            mask = (mask ?? "").Trim();
            if (mask.Length != 8) return -1;
            int n = 0;
            foreach (char c in mask)
            {
                if (c == 'X' || c == 'x' || c == '*' || c == '?') n++;
                else if (!Uri.IsHexDigit(c)) return -1;
            }
            return 1L << (4 * n);
        }

        // Parser slovniku z textu: hesla oddelena radky/carkami/mezerami, kazde 8 hex znaku.
        public static List<byte[]> ParseDictionaryText(string text)
        {
            var list = new List<byte[]>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (var tok in text.Split(new[] { '\r', '\n', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var h = new string(tok.Where(Uri.IsHexDigit).ToArray());
                if (h.Length != 8) continue;
                list.Add(new[] {
                    Convert.ToByte(h.Substring(0,2),16), Convert.ToByte(h.Substring(2,2),16),
                    Convert.ToByte(h.Substring(4,2),16), Convert.ToByte(h.Substring(6,2),16) });
            }
            return list;
        }

        // Chytry slovnik pravdepodobnych hesel pro vlastni tagy
        public static List<byte[]> PasswordDictionary(byte[] epc, byte[] tid)
        {
            var list = new List<byte[]>();
            void Add(uint v) => list.Add(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
            Add(0x00000000); Add(0xFFFFFFFF); Add(0x11111111); Add(0x12345678);
            Add(0x88888888); Add(0xAAAAAAAA); Add(0x12121212); Add(0x00000001);
            Add(0x11112222); Add(0x1234ABCD); Add(0xA5A5A5A5); Add(0x5A5A5A5A);
            Add(0x01234567); Add(0xABCDABCD); Add(0xDEADBEEF); Add(0xCAFEBABE);
            // z EPC (prvni/posledni 4 B)
            if (epc != null && epc.Length >= 4)
            {
                list.Add(epc.Take(4).ToArray());
                list.Add(epc.Skip(epc.Length - 4).Take(4).ToArray());
            }
            // z TID
            if (tid != null && tid.Length >= 4)
            {
                list.Add(tid.Take(4).ToArray());
                if (tid.Length >= 8) list.Add(tid.Skip(tid.Length - 4).Take(4).ToArray());
            }
            // de-duplikace
            var seen = new HashSet<string>();
            return list.Where(b => b != null && b.Length == 4 && seen.Add(BitConverter.ToString(b))).ToList();
        }

        // ---------------- TESTY ----------------

        // Test citlivosti: ramp vykonu, pro kazdy vykon kratka inventura cile.
        public List<(int power, double readRate, double rssi)> SensitivityTest(
            byte[] targetEpc, int minPow, int maxPow, double perStep, Action<string> progress, Func<bool> cancel = null)
        {
            var res = new List<(int, double, double)>();
            byte origPow = 17; { if (NativeApi.GetRFPower(_h, out var p, out _) == 0) origPow = p; }
            try
            {
                for (int pow = minPow; pow <= maxPow; pow++)
                {
                    if (cancel != null && cancel()) break;
                    NativeApi.SetRFPower(_h, (byte)pow, 0);
                    Thread.Sleep(40);
                    var found = Inventory(perStep, out int polls);
                    TagRecord t = targetEpc != null
                        ? found.Values.FirstOrDefault(x => TagDecode.SameBytes(x.Epc, targetEpc))
                        : found.Values.OrderByDescending(x => x.Rssi).FirstOrDefault();
                    double rate = t != null ? 100.0 * t.Count / Math.Max(polls, 1) : 0;
                    double rssi = t?.Rssi ?? 0;
                    res.Add((pow, rate, rssi));
                    progress?.Invoke(string.Format("{0,2} dBm: cteni {1,3:0} %  RSSI {2}", pow, rate, t != null ? rssi.ToString("0.0") : "-"));
                }
            }
            finally { NativeApi.SetRFPower(_h, origPow, 0); }
            return res;
        }

        // Kanalovy sken: delsi inventura, agregace po kanalech (frekvencni odezva)
        public List<ChannelStat> ChannelScan(double seconds, Action<string> progress)
        {
            var map = new Dictionary<int, (int cnt, double sum)>();
            NativeApi.InventoryContinue(_h, 0, 0);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (sw.Elapsed.TotalSeconds < seconds)
                {
                    if (NativeApi.GetTagUii(_h, out var tag, 200) != 0) continue;
                    if (tag.codeLen == 0) continue;
                    int ch = tag.channel;
                    var cur = map.TryGetValue(ch, out var e) ? e : (0, 0.0);
                    map[ch] = (cur.Item1 + 1, cur.Item2 + tag.rssi / 10.0);
                }
            }
            finally { NativeApi.InventoryStop(_h, 1000); }
            return map.OrderBy(k => k.Key)
                      .Select(k => new ChannelStat { Channel = k.Key, Count = k.Value.cnt, AvgRssi = k.Value.sum / k.Value.cnt })
                      .ToList();
        }

        // Soak test stability
        public SoakStat SoakTest(double seconds, Action<string> progress, Func<bool> cancel = null)
        {
            var rssis = new List<double>();
            var uniq = new HashSet<string>();
            int reads = 0, polls = 0;
            NativeApi.InventoryContinue(_h, 0, 0);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (sw.Elapsed.TotalSeconds < seconds)
                {
                    if (cancel != null && cancel()) break;
                    int r = NativeApi.GetTagUii(_h, out var tag, 200);
                    polls++;
                    if (r != 0 || tag.codeLen == 0) continue;
                    reads++;
                    rssis.Add(tag.rssi / 10.0);
                    var epc = new byte[tag.codeLen]; Array.Copy(tag.code, epc, tag.codeLen);
                    uniq.Add(TagDecode.Hex(epc, false));
                    if (reads % 100 == 0) progress?.Invoke(reads + " cteni...");
                }
            }
            finally { NativeApi.InventoryStop(_h, 1000); }
            double secs = sw.Elapsed.TotalSeconds;
            var st = new SoakStat
            {
                TotalReads = reads, UniqueTags = uniq.Count, Polls = polls, Seconds = secs,
                ReadsPerSec = reads / Math.Max(secs, 0.001),
            };
            if (rssis.Count > 0)
            {
                st.RssiMin = rssis.Min(); st.RssiMax = rssis.Max(); st.RssiAvg = rssis.Average();
                double avg = st.RssiAvg;
                st.RssiStd = Math.Sqrt(rssis.Sum(x => (x - avg) * (x - avg)) / rssis.Count);
            }
            st.Grade = st.ReadsPerSec > 150 ? "A (vyborne)" : st.ReadsPerSec > 80 ? "B (dobre)"
                     : st.ReadsPerSec > 30 ? "C (prumerne)" : st.ReadsPerSec > 5 ? "D (slabe)" : "F (nefunkcni)";
            return st;
        }

        // ---------------- KONFIGURACE ----------------

        public ReaderConfig GetFullConfig()
        {
            var c = new ReaderConfig();
            if (NativeApi.GetDevicePara(_h, out var p) == 0)
            {
                c.Raw = p; c.Region = p.REGION; c.Power = p.RFIDPOWER; c.QValue = p.QVALUE;
                c.Session = p.SESSION; c.Workmode = p.WORKMODE; c.Antenna = p.ANT;
                c.FreqStart = (p.STRATFREI[0] << 8) | p.STRATFREI[1];
                c.FreqStep = (p.STEPFRE[0] << 8) | p.STEPFRE[1];
            }
            if (NativeApi.GetRFIDType(_h, out var t) == 0) c.RfidType = t;
            if (NativeApi.GetTemperature(_h, out var cur, out var lim) == 0) { c.TempCur = cur; c.TempLimit = lim; }
            if (NativeApi.GetDeviceInfo(_h, out var di) == 0)
            {
                c.Firmware = AsciiZ(di.firmVersion); c.Hardware = AsciiZ(di.hardVersion);
                c.Sn = TagDecode.Hex(di.SN, false);
            }
            return c;
        }

        static string AsciiZ(byte[] b)
        {
            int n = Array.IndexOf(b, (byte)0); if (n < 0) n = b.Length;
            return System.Text.Encoding.ASCII.GetString(b, 0, n).Trim();
        }

        public OpResult SetPower(int dbm)
        {
            int res = NativeApi.SetRFPower(_h, (byte)dbm, 0);
            return new OpResult { Ok = res == 0, Status = res, Message = res == 0 ? "Vykon nastaven" : ErrorCodes.Text(res) };
        }

        // Nastaveni zakladnich parametru (region/vykon/Q/session/workmode) pres DevicePara
        public OpResult SetBasicConfig(int region, int power, int qvalue, int session, int workmode)
        {
            if (NativeApi.GetDevicePara(_h, out var p) != 0)
                return new OpResult { Ok = false, Message = "Nelze nacist aktualni parametry." };
            p.REGION = (byte)region; p.RFIDPOWER = (byte)power; p.QVALUE = (byte)qvalue;
            p.SESSION = (byte)session; p.WORKMODE = (byte)workmode;
            int res = NativeApi.SetDevicePara(_h, p);
            return new OpResult { Ok = res == 0, Status = res, Message = res == 0 ? "Konfigurace ulozena" : ErrorCodes.Text(res) };
        }

        public OpResult Reboot()
        {
            int res = NativeApi.RebootDevice(_h);
            return new OpResult { Ok = res == 0, Status = res, Message = res == 0 ? "Prikaz reboot odeslan" : ErrorCodes.Text(res) };
        }

        // ---------------- DUMP PAMETI ----------------
        public class BankDump { public Bank Bank; public byte[] Data; public bool Ok; public string Note; }

        public List<BankDump> FullMemoryDump(byte[] accPwd = null, byte[] selectEpc = null)
        {
            accPwd ??= new byte[4];
            if (selectEpc != null) SelectByEpc(selectEpc);
            var res = new List<BankDump>();
            // RESERVED: 4 slova; EPC: dle PC; TID: 6; USER: cteni po blocich
            res.Add(DumpBank(Bank.Reserved, 4, accPwd));
            res.Add(DumpBank(Bank.Epc, 8, accPwd));
            res.Add(DumpBank(Bank.Tid, 6, accPwd));
            // USER postupne
            var user = new List<byte>(); int ptr = 0; string note = null; bool ok = false;
            while (ptr < 128)
            {
                var r = ReadMemory(Bank.User, (ushort)ptr, 8, accPwd);
                if (!r.Ok) { if (ptr == 0) note = r.StatusText; break; }
                ok = true; user.AddRange(r.Data); ptr += 8;
                if (r.Data.Length < 16) break;
            }
            res.Add(new BankDump { Bank = Bank.User, Data = user.ToArray(), Ok = ok, Note = note });
            return res;
        }

        BankDump DumpBank(Bank b, byte words, byte[] accPwd)
        {
            var r = ReadMemory(b, 0, words, accPwd);
            return new BankDump { Bank = b, Data = r.Data, Ok = r.Ok, Note = r.Ok ? null : r.StatusText };
        }
    }
}
