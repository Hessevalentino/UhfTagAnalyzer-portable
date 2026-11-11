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
        public double Rssi;                       // posledni hodnota
        public double RssiSum, RssiMin = double.MaxValue, RssiMax = double.MinValue;
        public double RssiAvg => Count > 0 ? RssiSum / Count : Rssi;
        public void AddRssi(double v) { Rssi = v; RssiSum += v; if (v < RssiMin) RssiMin = v; if (v > RssiMax) RssiMax = v; }
        public byte Antenna;
        public byte Channel;
        public int Count;
        public int Polls;
        public string EpcHex => TagDecode.Hex(Epc, false);
    }

    public class DeviceInfo
    {
        public string Firmware, Hardware, Sn, RegionName;
        public int Power, Workmode, QValue, Session, Region = -1;
    }

    public class BankRead
    {
        public bool Ok;
        public byte[] Data = Array.Empty<byte>();
        public int Status;
        public byte[] Pc, Crc, Epc;          // PC/CRC/EPC tagu, ktery odpovedel (dle odpovedi ctecky)
        public string StatusText => ErrorCodes.Text(Status);
    }

    public class AnalysisResult
    {
        public bool Detected;
        public int TagCount;
        public TagRecord Tag;
        public PcInfo Pc;
        public bool PcKnown;            // PC/CRC se podarilo zjistit (z EPC banky)
        public bool EpcCrcOk;
        public TidInfo Tid;
        public bool TidReadable;
        public int TidStatus;
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

        // Nejnizsi RSSI, ktere CF601 hlasi (overeno: pri saturaci prijimace vraci presne -90.0 dBm).
        public const double RssiFloor = -90.0;

        // Vyber konkretniho tagu podle EPC. Ukazatel 0 = zacatek EPC kodu – stejne jako
        // vyrobcova aplikace Desk Reader (SetSelectMask(0, bity, EPC)).
        public int SelectByEpc(byte[] epc)
        {
            if (epc == null || epc.Length == 0) return ClearSelect();
            return NativeApi.SetSelectMask(_h, 0, (byte)(epc.Length * 8), epc);
        }

        // Zrusi vyber – operace pak cili na libovolny tag v poli.
        public int ClearSelect() => NativeApi.SetSelectMask(_h, 0, 0, Array.Empty<byte>());

        public static int UsbCount() => NativeApi.CFHid_GetUsbCount();

        public static string UsbInfo(ushort index)
        {
            var buf = new byte[256];
            NativeApi.CFHid_GetUsbInfo(index, buf);
            int n = Array.IndexOf(buf, (byte)0);
            if (n < 0) n = buf.Length;
            return Encoding.ASCII.GetString(buf, 0, n).Trim();
        }

        static string PortName(byte x) => x == 1 ? "USB" : x == 2 ? "KLAVESNICE" : x == 4 ? "CDC_COM" : ("? (" + x + ")");

        // Otevre HID spojeni s nekolika pokusy (ctecka obcas chvili po zapojeni jeste nabiha).
        bool TryOpen(ushort index, int tries)
        {
            for (int i = 0; i < tries; i++)
            {
                for (int w = 0; w < 10 && NativeApi.CFHid_GetUsbCount() <= 0; w++) Thread.Sleep(250);
                if (NativeApi.OpenHidConnection(out _h, index) == 0 && _h != IntPtr.Zero) return true;
                Thread.Sleep(250);
            }
            _h = IntPtr.Zero;
            return false;
        }

        // Studeny start: prvni prikazy po otevreni casto vrati -238 (timeout). Opakuj.
        int GetParaRetry(out DevicePara p, int tries = 16)
        {
            int r = -238; p = default;
            for (int i = 0; i < tries; i++)
            {
                r = NativeApi.GetDevicePara(_h, out p);
                if (r == 0) return 0;
                Thread.Sleep(180);
            }
            return r;
        }

        // Otevre ctecku, "zahreje" studeny start a ZAJISTI API rezim: PORT=USB a WORKMODE=answer.
        // Bez toho ctecka posila data jako klavesnice (HID) a API inventura/cteni nevrati nic.
        public void OpenAndPrepare(ushort index, Action<string> log = null)
        {
            void L(string s) => log?.Invoke(s);
            if (!TryOpen(index, 15))
                throw new Exception("OpenHidConnection selhalo – ctecka neodpovida (zkus ji odpojit a znovu zapojit).");
            IsOpen = true;

            int gp = GetParaRetry(out var p, 18);
            if (gp != 0)
            {
                L("POZOR: ctecka neodpovida na GetDevicePara (" + ErrorCodes.Text(gp) + ") – zkus odpojit/zapojit.");
                return;
            }

            if (p.INTERFACE != 1 || p.WORKMODE != 0)
            {
                L(string.Format("Rezim ctecky: PORT={0}, WORKMODE={1}. Prepinam na USB + answer (nutne pro cteni pres API)...",
                    PortName(p.INTERFACE), p.WORKMODE));
                p.INTERFACE = 1; p.WORKMODE = 0;
                NativeApi.SetDevicePara(_h, p);
                Thread.Sleep(600);

                // Zmena PORTu zmeni USB deskriptor -> ctecka se re-enumeruje a handle padne. Reconnect.
                int gp2 = GetParaRetry(out var p2, 5);
                if (gp2 != 0)
                {
                    L("Ctecka se po zmene rezimu znovu prihlasuje na USB – pripojuji znovu...");
                    try { NativeApi.CloseDevice(_h); } catch { }
                    Thread.Sleep(1800);
                    if (!TryOpen(index, 15))
                        throw new Exception("Po zmene rezimu se ctecku nepodarilo znovu otevrit – odpoj a zapoj ji a klikni Pripojit.");
                    gp2 = GetParaRetry(out p2, 18);
                }

                if (gp2 == 0 && p2.INTERFACE == 1 && p2.WORKMODE == 0)
                    L("Rezim nastaven: USB + answer. Ctecka je pripravena.");
                else
                    L("Rezim se nepodarilo plne overit (PORT=" + (gp2 == 0 ? PortName(p2.INTERFACE) : "?") +
                      "). Zkus Reboot ctecky v zalozce Konfigurace a pripoj znovu.");
            }
            else
            {
                L("Rezim ctecky: USB + answer. Ctecka je pripravena.");
            }
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
                // Device* = cela ctecka (stabilni FW/HW/SN); hard/firm/SN = vnitrni RF modul
                // (na CF601 je FW modulu prazdne a SN modulu nestabilni).
                di.Firmware = FirstNonEmpty(CStr(info.DevicefirmVersion), CStr(info.firmVersion));
                string devHw = CStr(info.DevicehardVersion), modHw = CStr(info.hardVersion);
                di.Hardware = string.IsNullOrEmpty(modHw) ? devHw : (string.IsNullOrEmpty(devHw) ? modHw : devHw + " / modul " + modHw);
                di.Sn = TagDecode.Hex(info.DeviceSN, false);
            }
            if (NativeApi.GetDevicePara(_h, out var p) == 0)
            {
                di.Power = p.RFIDPOWER; di.Workmode = p.WORKMODE;
                di.QValue = p.QVALUE; di.Session = p.SESSION;
                di.Region = p.REGION;
                di.RegionName = TagDecode.RegionName(p.REGION);
            }
            return di;
        }

        static string FirstNonEmpty(params string[] s) => s.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "";

        static string CStr(byte[] b)
        {
            int n = Array.IndexOf(b, (byte)0);
            if (n < 0) n = b.Length;
            return Encoding.ASCII.GetString(b, 0, n).Trim();
        }

        // Spolecna inventurni smycka (Inventory, kanalovy sken, soak test).
        // Protokol dle vyrobce (C# sample InventoryThread): InventoryContinue, pak GetTagUii:
        //   0     = tag (EPC/RSSI),
        //   -238  = timeout komunikace -> ignorovat a cist dal,
        //   -249  = v poli zadny tag / inventura skoncila -> KONEC (dalsi volani by jen cekala ~2 s).
        // Vraci false, kdyz se inventuru nepodarilo spustit.
        bool InventoryLoop(double seconds, int minPolls, Action<TagInfo> onTag, Func<bool> cancel, out int polls)
        {
            polls = 0;
            // Probuzeni: ctecka po chvili neaktivity "usne", prvni inventura pak vraci -238.
            // Neuspesne volani s -238 samo trva ~2 s (interni timeout DLL) -> max 4 pokusy.
            NativeApi.GetDevicePara(_h, out _);
            int ic = -238;
            for (int i = 0; i < 4 && ic != 0; i++) { ic = NativeApi.InventoryContinue(_h, 0, 0); if (ic != 0) Thread.Sleep(200); }
            if (ic != 0) { try { NativeApi.InventoryStop(_h, 1000); } catch { } return false; }

            var sw = Stopwatch.StartNew();
            // Bez tagu v dosahu DLL casto nevraci -249, ale -238 az po ~2 s -> minPolls by se
            // nasobilo (6 pokusu = 12 s). Pevny strop: zadany cas + 3 s.
            double hardCap = seconds + 3.0;
            try
            {
                // do vyprseni casu, ale aspon minPolls pokusu (absorbuje -238 po studenem startu)
                while ((sw.Elapsed.TotalSeconds < seconds || polls < minPolls) && sw.Elapsed.TotalSeconds < hardCap)
                {
                    if (cancel != null && cancel()) break;
                    int res = NativeApi.GetTagUii(_h, out var tag, 300);
                    polls++;
                    if (res == -249) break;                     // zadny tag / konec inventury
                    if (res != 0) continue;
                    if (tag.codeLen <= 0 || tag.code == null) continue;
                    onTag(tag);
                }
            }
            finally { NativeApi.InventoryStop(_h, 1000); }
            return true;
        }

        public Dictionary<string, TagRecord> Inventory(double seconds, out int polls, int minPolls = 0)
        {
            var found = new Dictionary<string, TagRecord>();
            InventoryLoop(seconds, minPolls, tag =>
            {
                int n = Math.Min((int)tag.codeLen, tag.code.Length);
                var epc = new byte[n];
                Array.Copy(tag.code, epc, n);
                string key = TagDecode.Hex(epc, false);
                if (!found.TryGetValue(key, out var rec))
                {
                    rec = new TagRecord
                    {
                        Epc = epc,
                        Pc = (byte[])tag.pc.Clone(),
                        Crc = (byte[])tag.crc.Clone(),
                        Antenna = tag.antenna,
                        Channel = tag.channel,
                    };
                    found[key] = rec;
                }
                rec.Count++;
                rec.AddRssi(tag.rssi / 10.0);
            }, null, out polls);
            foreach (var r in found.Values) r.Polls = polls;
            return found;
        }

        // Cteni pameti – protokol overeny trasovanim na CF601 (fw UCM601-v221):
        //   ReadTag -> GetReadTagResp vrati pro kazdy tag v poli jednu odpoved
        //   (0 = data, tagStatus je pri uspechu 0xFF; nebo zaporny kod chyby od tagu,
        //   napr. -244 = banka zamcena heslem), a nakonec UKONCOVACI -249 (~8 ms).
        //   Po -249 uz neprijde nic (dalsi volani ceka ~2 s a vrati -238).
        // Frontu je nutne docist az po -249, jinak zbytek posune dalsi cteni o jeden pozadavek.
        public BankRead ReadMemory(Bank bank, ushort wordPtr, byte wordCount,
                                   byte[] accPwd = null, int retries = 2, byte[] expectEpc = null)
        {
            accPwd ??= new byte[4];
            var r = new BankRead { Status = -249 };
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                int res = NativeApi.ReadTag(_h, 0, accPwd, (byte)bank, wordPtr, wordCount);
                if (res != 0) { r.Status = res; Thread.Sleep(60); continue; }

                bool terminated = false, gotTagError = false;
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 3000)
                {
                    var data = new byte[512];
                    int res2 = NativeApi.GetReadTagResp(_h, out var resp, out byte wc, data, 1000);
                    if (res2 == -249) { terminated = true; break; }       // konec operace
                    if (res2 == -238) break;                               // nic dalsiho neprislo
                    if (res2 == -248 || res2 == -241) continue;            // prechodne – cekej dal
                    if (res2 != 0)
                    {
                        // -250/-247/-246 jsou prechodne (interni chyba / kolize) -> povol opakovani;
                        // ostatni (napr. -244 heslo, -189 zamek) jsou odpoved tagu -> opakovani nepomuze
                        if (!r.Ok) { r.Status = res2; gotTagError = res2 != -250 && res2 != -247 && res2 != -246; }
                        continue;
                    }

                    // datova odpoved jednoho tagu
                    int cl = Math.Min((int)resp.codeLen, resp.code?.Length ?? 0);
                    var epc = resp.code?.Take(cl).ToArray() ?? Array.Empty<byte>();
                    if (r.Ok) continue;                                    // uz mame; jen docitame frontu
                    if (expectEpc != null && epc.Length > 0 && !TagDecode.SameBytes(epc, expectEpc)) continue;
                    int nbytes = wc * 2;
                    if (nbytes < 0 || nbytes > data.Length) nbytes = 0;
                    r.Ok = true; r.Status = 0;
                    r.Data = data.Take(nbytes).ToArray();
                    r.Pc = (byte[])resp.pc?.Clone();
                    r.Crc = (byte[])resp.crc?.Clone();
                    r.Epc = epc;
                }
                if (!terminated) { try { NativeApi.InventoryStop(_h, 1000); } catch { } }
                if (r.Ok) return r;
                if (gotTagError) break;   // tag odpovida, ale odmita (zamek/heslo) – opakovani nepomuze
            }
            return r;
        }

        // Precte banku az do jejiho konce (velikost banky predem nezname): bloky po 8 slovech;
        // kdyz blok pretece za konec pameti, zmensi se (4, 2, 1). Konci, kdyz neprojde ani 1 slovo.
        public BankRead ReadBankAll(Bank bank, int maxWords, byte[] accPwd = null, byte[] expectEpc = null)
        {
            var all = new List<byte>();
            ushort ptr = 0;
            byte chunk = 8;
            BankRead fail = null;
            while (ptr < maxWords)
            {
                byte want = (byte)Math.Min(chunk, maxWords - ptr);
                var br = ReadMemory(bank, ptr, want, accPwd, expectEpc: expectEpc);
                if (!br.Ok || br.Data.Length == 0)
                {
                    if (want > 1) { chunk = (byte)(want / 2); continue; }
                    fail = br;
                    break;
                }
                all.AddRange(br.Data);
                ptr += want;
                if (br.Data.Length < want * 2) break;
            }
            return all.Count > 0 ? new BankRead { Ok = true, Data = all.ToArray() } : (fail ?? new BankRead { Status = -249 });
        }

        // ---- kompletni analyza jednoho tagu ----
        public AnalysisResult Analyze(double scanSeconds, byte tidWords, byte maxUserWords,
                                      Action<string> log = null)
        {
            void L(string s) => log?.Invoke(s);
            var res = new AnalysisResult();

            ClearSelect();   // zbytkova maska z predchozi operace by skryla jiny tag
            L("Inventura " + scanSeconds.ToString("0.0") + " s ...");
            var found = Inventory(scanSeconds, out int polls, 10);
            if (found.Count == 0)
            {
                res.Detected = false;
                res.Report = BuildReport(res, null);
                L("Zadny tag nenalezen.");
                return res;
            }

            // nejcasteji cteny tag (RSSI je u CF601 kvuli saturaci nespolehlive – jen druhe kriterium)
            var tags = found.Values.OrderByDescending(t => t.Count).ThenByDescending(t => t.RssiAvg).ToList();
            res.Detected = true;
            res.TagCount = tags.Count;
            var tag = tags[0];
            res.Tag = tag;
            if (tags.Count > 1) L("POZOR: v poli " + tags.Count + " tagu, analyzuji nejcasteji cteny.");
            L("EPC: " + tag.EpcHex);

            // Po inventure MUSI cteni bezet s maskou na EPC tagu. Ctecka ma tovarne SESSION 2: inventura
            // prepne tagu priznak A->B, ten chvili pretrva a ReadTag bez masky tag nenajde.
            // Zmereno: S2 bez masky 0/10 cteni, s maskou 10/10 (stejne jako vyrobcova aplikace).
            SelectByEpc(tag.Epc);
            try
            {
            // CF601 v inventure neposila PC ani CRC (chodi 0000) – precti je primo z EPC banky:
            // slovo 0 = ulozene CRC-16, slovo 1 = PC.
            L("Ctu PC + CRC (EPC banka) ...");
            var pcCrc = ReadMemory(Bank.Epc, 0, 2, expectEpc: tag.Epc);
            if (pcCrc.Ok && pcCrc.Data.Length >= 4)
            {
                tag.Crc = pcCrc.Data.Take(2).ToArray();
                tag.Pc = pcCrc.Data.Skip(2).Take(2).ToArray();
                res.PcKnown = true;
            }
            else res.PcKnown = !(tag.Pc.All(b => b == 0) && tag.Crc.All(b => b == 0));
            res.Pc = TagDecode.DecodePc(tag.Pc);
            var calc = TagDecode.Gen2Crc16(tag.Pc, tag.Epc);
            res.EpcCrcOk = res.PcKnown && TagDecode.SameBytes(calc, tag.Crc);

            L("Ctu TID ...");
            var tid = ReadMemory(Bank.Tid, 0, tidWords, expectEpc: tag.Epc);
            if (!tid.Ok && tidWords > 2) tid = ReadMemory(Bank.Tid, 0, 2, expectEpc: tag.Epc);
            res.TidReadable = tid.Ok && tid.Data.Length >= 4;
            res.TidStatus = tid.Status;
            if (res.TidReadable) res.Tid = TagDecode.DecodeTid(tid.Data);

            L("Ctu RESERVED (hesla) ...");
            res.Reserved = ReadMemory(Bank.Reserved, 0, 4, expectEpc: tag.Epc);
            if (res.Reserved.Ok && res.Reserved.Data.Length >= 8)
            {
                res.KillPwd = res.Reserved.Data.Take(4).ToArray();
                res.AccessPwd = res.Reserved.Data.Skip(4).Take(4).ToArray();
            }

            L("Ctu USER pamet ...");
            res.User = ReadBankAll(Bank.User, maxUserWords, expectEpc: tag.Epc);
            }
            finally { ClearSelect(); }

            // verdikt
            double readRate = 100.0 * tag.Count / Math.Max(polls, 1);
            res.Checks.Add(("Detekovan v inventure", true));
            res.Checks.Add(("Stabilni cteni (>35 %)", readRate > 35));
            res.Checks.Add(("TID citelne (cip odpovida)", res.TidReadable));
            if (res.PcKnown) res.Checks.Add(("EPC CRC konzistentni", res.EpcCrcOk));
            // RSSI se do verdiktu NEPOCITA: CF601 pri plnem vykonu s tagem primo na ctecce
            // saturuje prijimac a hlasi nesmyslne -90 dBm (overeno: pri 3-5 dBm je RSSI -22 dBm).
            // Spolehlive meritko sily odezvy je uspesnost cteni + Test citlivosti.
            res.ScoreOk = res.Checks.Count(c => c.ok);
            res.ScoreTotal = res.Checks.Count;
            bool crcOk = !res.PcKnown || res.EpcCrcOk;   // nezname PC netrestame
            res.Verdict = (res.TidReadable && readRate > 35 && crcOk) ? "FUNKCNI"
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
                sb.AppendLine("- Priloz tag na ctecku (dosah 1-35 cm).");
                sb.AppendLine("- Kdyz tag lezi na ctecce a stale nic: odpoj ctecku z USB, zapoj znovu a Pripoj.");
                return sb.ToString();
            }

            double readRate = 100.0 * tag.Count / Math.Max(tag.Polls, 1);
            string q = readRate > 70 ? "vyborny" : readRate > 35 ? "dobry" : readRate > 10 ? "slaby" : "velmi slaby";

            sb.AppendLine();
            sb.AppendLine("[1] DETEKCE");
            sb.AppendLine("    Tagu v poli : " + r.TagCount);
            sb.AppendLine(string.Format("    Uspesnost   : {0}/{1} odezev ({2:0} %) -> signal {3}", tag.Count, tag.Polls, readRate, q));
            sb.AppendLine(string.Format("    EPC         : {0}  ({1} B / {2} bit)", TagDecode.Hex(tag.Epc), tag.Epc.Length, tag.Epc.Length * 8));
            sb.AppendLine(string.Format("    RSSI        : prumer {0:0.0} dBm  (min {1:0.0} / max {2:0.0})   Antena: {3}",
                tag.RssiAvg, tag.RssiMin, tag.RssiMax, tag.Antenna));
            if (tag.RssiMax <= RssiFloor + 0.5 && readRate > 35)
            {
                // vsechna cteni na spodni mezi ctecky, pritom tag cte spolehlive = saturace prijimace
                sb.AppendLine("                  !! RSSI na spodni mezi ctecky (-90) pri spolehlivem cteni = SATURACE:");
                sb.AppendLine("                     tag je prilis blizko anteny. Hodnota neplati; odsun tag o par cm");
                sb.AppendLine("                     nebo zmer silu odezvy Testem citlivosti.");
            }
            else
                sb.AppendLine("                  (orientacne; tag primo na ctecce muze prijimac zahltit – viz Test citlivosti)");
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
            if (r.PcKnown)
                sb.AppendLine(string.Format("    EPC CRC-16  : ulozeno {0} / spocteno {1} -> {2}",
                    TagDecode.Hex(tag.Crc, false), TagDecode.Hex(calc, false),
                    r.EpcCrcOk ? "OK" : "nesouhlasi"));
            else
                sb.AppendLine("    EPC CRC-16  : nelze overit (PC/CRC se nepodarilo precist z EPC banky)");

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
                if (r.Tid.ModelMemory != null) sb.AppendLine("    Pamet cipu  : " + r.Tid.ModelMemory + "  (dle datasheetu)");
                if (r.Tid.ModelNote != null) sb.AppendLine("    Pozn.       : " + r.Tid.ModelNote);
                if (r.Tid.Xtid)
                {
                    var xt = TagDecode.ParseXtid(r.Tid.RawBytes);
                    if (xt != null) foreach (var l in xt.Split('\n')) sb.AppendLine("    " + l.TrimEnd());
                }
            }
            else
            {
                sb.AppendLine("    TID nelze precist: " + ErrorCodes.Text(r.TidStatus));
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
            else if (r.Tid?.Model == "Monza R6")
            {
                sb.AppendLine("    Nelze precist – Monza R6 hesla nema (RESERVED je trvale nectitelna).");
                sb.AppendLine("    -> Tag NEMA nastavene heslo; obnova hesla nema smysl.");
            }
            else if (r.Reserved != null && (r.Reserved.Status == -244 || r.Reserved.Status == -188
                     || r.Reserved.Status == -189 || r.Reserved.Status == -172))
            {
                sb.AppendLine("    ZAMCENO HESLEM – tag odmitl cteni s access heslem 00000000.");
                sb.AppendLine("    -> Tag ma nastavene access heslo a RESERVED banku zamcenou pro cteni.");
                sb.AppendLine("       Hesla vycteme jen se spravnym access heslem (zalozka Pamet).");
            }
            else
            {
                sb.AppendLine("    Nelze precist: " + (r.Reserved?.StatusText ?? "?"));
                sb.AppendLine("    -> Tag RESERVED banku nema, nebo je slaby signal.");
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
