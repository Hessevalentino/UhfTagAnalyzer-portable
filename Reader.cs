using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace UhfTagAnalyzer
{
    public class TagRecord
    {
        public byte[] Epc;
        public byte[] Pc = new byte[2];
        public byte[] Crc = new byte[2];
        public double Rssi;
        public byte Antenna;
        public byte Channel;
        public int Count;
        public int Polls;
        public string EpcHex => TagDecode.Hex(Epc, false);
    }

    public class DeviceInfo
    {
        public string Firmware, Hardware, Sn, RegionName;
        public int Power, Workmode, QValue, Session;
    }

    public class BankRead
    {
        public bool Ok;
        public byte[] Data = Array.Empty<byte>();
        public int Status;
        public string StatusText => ErrorCodes.Text(Status);
    }

    public class AnalysisResult
    {
        public bool Detected;
        public int TagCount;
        public TagRecord Tag;
        public PcInfo Pc;
        public bool EpcCrcOk;
        public TidInfo Tid;
        public bool TidReadable;
        public BankRead Reserved;
        public BankRead User;
        public byte[] KillPwd, AccessPwd;
        public string Verdict;
        public int ScoreOk, ScoreTotal;
        public List<(string name, bool ok)> Checks = new List<(string, bool)>();
        public string Report; // hotovy textovy report
    }

    public partial class CF601Reader : IDisposable
    {
        IntPtr _h = IntPtr.Zero;
        public bool IsOpen { get; private set; }

        // vyber konkretniho tagu podle EPC (maska zacina na bitu 0x20 v EPC bance)
        public void SelectByEpc(byte[] epc)
        {
            if (epc == null || epc.Length == 0) return;
            NativeApi.SetSelectMask(_h, 0x20, (byte)(epc.Length * 8), epc);
        }

        public static int UsbCount() => NativeApi.CFHid_GetUsbCount();

        public static string UsbInfo(ushort index)
        {
            var buf = new byte[256];
            NativeApi.CFHid_GetUsbInfo(index, buf);
            int n = Array.IndexOf(buf, (byte)0);
            if (n < 0) n = buf.Length;
            return Encoding.ASCII.GetString(buf, 0, n).Trim();
        }

        public void Open(ushort index = 0)
        {
            int res = NativeApi.OpenHidConnection(out _h, index);
            if (res != 0 || _h == IntPtr.Zero)
                throw new Exception("OpenHidConnection selhalo: " + ErrorCodes.Text(res));
            IsOpen = true;
        }

        public void Close()
        {
            if (IsOpen)
            {
                try { NativeApi.CloseDevice(_h); } catch { }
                IsOpen = false; _h = IntPtr.Zero;
            }
        }

        public void Dispose() => Close();

        public DeviceInfo GetDeviceInfo()
        {
            var di = new DeviceInfo();
            if (NativeApi.GetDeviceInfo(_h, out var info) == 0)
            {
                di.Firmware = CStr(info.firmVersion);
                di.Hardware = CStr(info.hardVersion);
                di.Sn = TagDecode.Hex(info.SN, false);
            }
            if (NativeApi.GetDevicePara(_h, out var p) == 0)
            {
                di.Power = p.RFIDPOWER; di.Workmode = p.WORKMODE;
                di.QValue = p.QVALUE; di.Session = p.SESSION;
                di.RegionName = TagDecode.RegionName(p.REGION);
            }
            return di;
        }

        static string CStr(byte[] b)
        {
            int n = Array.IndexOf(b, (byte)0);
            if (n < 0) n = b.Length;
            return Encoding.ASCII.GetString(b, 0, n).Trim();
        }

        public Dictionary<string, TagRecord> Inventory(double seconds, out int polls)
        {
            NativeApi.InventoryContinue(_h, 0, 0);
            var found = new Dictionary<string, TagRecord>();
            polls = 0;
            var sw = Stopwatch.StartNew();
            try
            {
                while (sw.Elapsed.TotalSeconds < seconds)
                {
                    int res = NativeApi.GetTagUii(_h, out var tag, 200);
                    polls++;
                    if (res != 0) continue;
                    int n = tag.codeLen;
                    if (n <= 0 || n > 255) continue;
                    var epc = new byte[n];
                    Array.Copy(tag.code, epc, n);
                    string key = TagDecode.Hex(epc, false);
                    if (found.TryGetValue(key, out var rec))
                    {
                        rec.Count++;
                        rec.Rssi = tag.rssi / 10.0;
                    }
                    else
                    {
                        found[key] = new TagRecord
                        {
                            Epc = epc,
                            Pc = (byte[])tag.pc.Clone(),
                            Crc = (byte[])tag.crc.Clone(),
                            Rssi = tag.rssi / 10.0,
                            Antenna = tag.antenna,
                            Channel = tag.channel,
                            Count = 1,
                        };
                    }
                }
            }
            finally { NativeApi.InventoryStop(_h, 1000); }
            foreach (var r in found.Values) r.Polls = polls;
            return found;
        }

        public BankRead ReadMemory(Bank bank, ushort wordPtr, byte wordCount,
                                   byte[] accPwd = null, int retries = 2)
        {
            accPwd ??= new byte[4];
            var r = new BankRead { Status = -248 };
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                int res = NativeApi.ReadTag(_h, 0, accPwd, (byte)bank, wordPtr, wordCount);
                if (res != 0) { r.Status = res; Thread.Sleep(30); continue; }
                var data = new byte[512];
                int res2 = NativeApi.GetReadTagResp(_h, out var resp, out byte wc, data, 800);
                if (res2 == 0)
                {
                    int nbytes = wc * 2;
                    if (nbytes <= 0 || nbytes > data.Length) nbytes = wordCount * 2;
                    r.Ok = true; r.Status = 0;
                    r.Data = data.Take(nbytes).ToArray();
                    return r;
                }
                r.Status = res2; Thread.Sleep(30);
            }
            return r;
        }

        // ---- kompletni analyza jednoho tagu ----
        public AnalysisResult Analyze(double scanSeconds, byte tidWords, byte maxUserWords,
                                      Action<string> log = null)
        {
            void L(string s) => log?.Invoke(s);
            var res = new AnalysisResult();

            L("Inventura " + scanSeconds.ToString("0.0") + " s ...");
            var found = Inventory(scanSeconds, out int polls);
            if (found.Count == 0)
            {
                res.Detected = false;
                res.Report = BuildReport(res, null);
                L("Zadny tag nenalezen.");
                return res;
            }

            var tags = found.Values.OrderByDescending(t => t.Rssi).ToList();
            res.Detected = true;
            res.TagCount = tags.Count;
            var tag = tags[0];
            res.Tag = tag;
            if (tags.Count > 1) L("POZOR: v poli " + tags.Count + " tagu, analyzuji nejsilnejsi.");
            L("EPC: " + tag.EpcHex);

            res.Pc = TagDecode.DecodePc(tag.Pc);
            var calc = TagDecode.Gen2Crc16(tag.Pc, tag.Epc);
            res.EpcCrcOk = TagDecode.SameBytes(calc, tag.Crc);

            L("Ctu TID ...");
            var tid = ReadMemory(Bank.Tid, 0, tidWords);
            if (!tid.Ok && tidWords > 2) tid = ReadMemory(Bank.Tid, 0, 2);
            res.TidReadable = tid.Ok && tid.Data.Length >= 4;
            if (res.TidReadable) res.Tid = TagDecode.DecodeTid(tid.Data);

            L("Ctu RESERVED (hesla) ...");
            res.Reserved = ReadMemory(Bank.Reserved, 0, 4);
            if (res.Reserved.Ok && res.Reserved.Data.Length >= 8)
            {
                res.KillPwd = res.Reserved.Data.Take(4).ToArray();
                res.AccessPwd = res.Reserved.Data.Skip(4).Take(4).ToArray();
            }

            L("Ctu USER pamet ...");
            var all = new List<byte>();
            ushort ptr = 0;
            const byte chunk = 8;
            while (ptr < maxUserWords)
            {
                byte want = (byte)Math.Min(chunk, maxUserWords - ptr);
                var br = ReadMemory(Bank.User, ptr, want);
                if (!br.Ok || br.Data.Length == 0) { res.User = br; break; }
                all.AddRange(br.Data);
                ptr += want;
                res.User = br;
                if (br.Data.Length < want * 2) break;
            }
            if (all.Count > 0) { res.User = new BankRead { Ok = true, Data = all.ToArray() }; }

            // verdikt
            double readRate = 100.0 * tag.Count / Math.Max(polls, 1);
            res.Checks.Add(("Detekovan v inventure", true));
            res.Checks.Add(("Stabilni cteni (>35 %)", readRate > 35));
            res.Checks.Add(("TID citelne (cip odpovida)", res.TidReadable));
            res.Checks.Add(("EPC CRC konzistentni", res.EpcCrcOk));
            res.Checks.Add(("Dostatecny signal (RSSI > -65 dBm)", tag.Rssi > -65));
            res.ScoreOk = res.Checks.Count(c => c.ok);
            res.ScoreTotal = res.Checks.Count;
            res.Verdict = (res.TidReadable && readRate > 35 && res.EpcCrcOk) ? "FUNKCNI"
                        : (res.TidReadable || readRate > 10) ? "CASTECNE FUNKCNI / slaby signal"
                        : "NEFUNKCNI / poskozeny nebo mimo dosah";

            res.Report = BuildReport(res, tag);
            L("Hotovo: " + res.Verdict);
            return res;
        }

        static string BuildReport(AnalysisResult r, TagRecord tag)
        {
            var sb = new StringBuilder();
            string line = new string('=', 60);
            sb.AppendLine(line);
            sb.AppendLine(" CF601 UHF TAG ANALYZER – report  (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ")");
            sb.AppendLine(line);

            if (!r.Detected)
            {
                sb.AppendLine();
                sb.AppendLine("ZADNY tag nenalezen.");
                sb.AppendLine("- Priloz tag blize ke ctecce (dosah 1-35 cm).");
                sb.AppendLine("- Zkontroluj, ze ctecka neni v klavesnicovem rezimu (WORKMODE).");
                return sb.ToString();
            }

            double readRate = 100.0 * tag.Count / Math.Max(tag.Polls, 1);
            string q = readRate > 70 ? "vyborny" : readRate > 35 ? "dobry" : readRate > 10 ? "slaby" : "velmi slaby";

            sb.AppendLine();
            sb.AppendLine("[1] DETEKCE");
            sb.AppendLine("    Tagu v poli : " + r.TagCount);
            sb.AppendLine(string.Format("    Uspesnost   : {0}/{1} odezev ({2:0} %) -> signal {3}", tag.Count, tag.Polls, readRate, q));
            sb.AppendLine(string.Format("    EPC         : {0}  ({1} B / {2} bit)", TagDecode.Hex(tag.Epc), tag.Epc.Length, tag.Epc.Length * 8));
            sb.AppendLine(string.Format("    RSSI        : {0:0.0} dBm   Antena: {1}   Kanal: {2}", tag.Rssi, tag.Antenna, tag.Channel));
            foreach (var l in TagDecode.InterpretEpc(tag.Epc).Split('\n'))
                sb.AppendLine("    " + l.TrimEnd());

            sb.AppendLine();
            sb.AppendLine("[2] PC SLOVO");
            if (r.Pc != null)
            {
                sb.AppendLine("    PC          : " + r.Pc.PcHex);
                sb.AppendLine(string.Format("    Delka EPC   : {0} slov ({1} bit)", r.Pc.EpcWords, r.Pc.EpcBits));
                sb.AppendLine("    User pamet  : " + (r.Pc.Umi ? "ANO (UMI=1)" : "ne (UMI=0)"));
                sb.AppendLine("    XPC         : " + (r.Pc.Xpc ? "ano" : "ne"));
                sb.AppendLine(string.Format("    Schema      : {0} (toggle={1})", r.Pc.Scheme, r.Pc.Toggle));
            }
            var calc = TagDecode.Gen2Crc16(tag.Pc, tag.Epc);
            sb.AppendLine(string.Format("    EPC CRC-16  : nacteno {0} / spocteno {1} -> {2}",
                TagDecode.Hex(tag.Crc, false), TagDecode.Hex(calc, false),
                r.EpcCrcOk ? "OK" : "nesouhlasi"));

            sb.AppendLine();
            sb.AppendLine("[3] TID – identifikace cipu");
            if (r.TidReadable && r.Tid != null)
            {
                sb.AppendLine("    Raw TID     : " + r.Tid.Raw);
                sb.AppendLine(string.Format("    Trida       : {0} = {1}", r.Tid.ClassHex, r.Tid.ClassDesc));
                sb.AppendLine(string.Format("    Vyrobce     : {0} (MDID {1})", r.Tid.Vendor, r.Tid.Mdid));
                sb.AppendLine(r.Tid.Model != null
                    ? string.Format("    Model       : {0} (TMN {1})", r.Tid.Model, r.Tid.Tmn)
                    : string.Format("    Model TMN   : {0} (neni v tabulce – over raw TID)", r.Tid.Tmn));
                sb.AppendLine(string.Format("    XTID        : {0}   Serializovane: {1}",
                    r.Tid.Xtid ? "ano" : "ne", r.Tid.Serialized ? "ano" : "ne"));
                sb.AppendLine("    >> CIP      : " + r.Tid.Summary);
                if (r.Tid.Xtid)
                {
                    var xt = TagDecode.ParseXtid(r.Tid.RawBytes);
                    if (xt != null) foreach (var l in xt.Split('\n')) sb.AppendLine("    " + l.TrimEnd());
                }
            }
            else
            {
                sb.AppendLine("    TID nelze precist: " + (r.TidReadable ? "?" : ErrorCodes.Text(-248)));
                sb.AppendLine("    -> TID je z vyroby trvale zamcene; neuspech casto znaci slaby signal nebo vadny cip.");
            }

            sb.AppendLine();
            sb.AppendLine("[4] RESERVED – kill / access heslo");
            if (r.KillPwd != null)
            {
                string Pw(byte[] x) => TagDecode.Hex(x, false) + (x.All(b => b == 0) ? "  (default/nenastaveno)" : "  (NASTAVENO)");
                sb.AppendLine("    Kill heslo  : " + Pw(r.KillPwd));
                sb.AppendLine("    Access heslo: " + Pw(r.AccessPwd));
            }
            else
            {
                sb.AppendLine("    Nelze precist: " + (r.Reserved?.StatusText ?? "?"));
                sb.AppendLine("    -> Bank je zamcena (hesla chranena) nebo ji tag nema / slaby signal.");
            }

            sb.AppendLine();
            sb.AppendLine("[5] USER pamet");
            if (r.User != null && r.User.Ok && r.User.Data.Length > 0)
            {
                var d = r.User.Data;
                sb.AppendLine(string.Format("    Velikost    : >= {0} slov ({1} B)", d.Length / 2, d.Length));
                sb.AppendLine("    Data (hex)  : " + TagDecode.Hex(d.Take(64).ToArray()));
                sb.AppendLine("    ASCII       : " + TagDecode.AsciiPreview(d.Take(64).ToArray()));
            }
            else
            {
                sb.AppendLine("    Zadna uzivatelska pamet nactena.");
                sb.AppendLine("    -> Mnoho cipu ma USER malou/zadnou. Viz UMI bit v [2].");
            }

            sb.AppendLine();
            sb.AppendLine("[6] VERDIKT");
            foreach (var c in r.Checks)
                sb.AppendLine("    [" + (c.ok ? "OK " : " ! ") + "] " + c.name);
            sb.AppendLine();
            sb.AppendLine(string.Format("    ZAVER: Tag je {0}  ({1}/{2} kontrol OK)", r.Verdict, r.ScoreOk, r.ScoreTotal));
            sb.AppendLine(line);
            return sb.ToString();
        }
    }
}
