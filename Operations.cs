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
        public int Region, Power, QValue, Session, Workmode, Antenna, RfidType, Port;
        public double FreqStartMHz, FreqEndMHz;   // frekvencni plan z DevicePara
        public int FreqStepKHz, FreqChannels;
        public int TempCur, TempLimit;
        public string Firmware, Hardware, Sn;
        public DevicePara Raw;
    }

    public class SoakStat
    {
        public int TotalReads, UniqueTags, Polls;
        public double Seconds, ReadsPerSec, RssiMin, RssiMax, RssiAvg, RssiStd, SuccessPct;
        public string Grade;
    }

    public partial class CF601Reader
    {
        // ---------------- ZAPIS / OPERACE ----------------

        const ushort CMD_WRITE = 0x0004, CMD_LOCK = 0x0005, CMD_KILL = 0x0006;

        // Navratovka WriteTag/LockTag/KillTag znamena jen "prikaz prijat". Skutecny vysledek
        // poslou tagy pres GetTagResp: 0 = uspech, zaporny kod = tag odmitl (heslo, zamek...),
        // nakonec ukoncovaci -249. Frontu docti az do konce, jinak rozhodi dalsi operace.
        OpResult CollectTagResp(ushort cmd, int sendRes, byte[] expectEpc, string okMsg)
        {
            if (sendRes != 0)
                return new OpResult { Ok = false, Status = sendRes, Message = ErrorCodes.Text(sendRes) };
            int status = -249; bool ok = false, terminated = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 3000)
            {
                int r = NativeApi.GetTagResp(_h, cmd, out var resp, 1000);
                if (r == -249) { terminated = true; break; }     // konec operace
                if (r == -238) break;                             // nic dalsiho neprislo
                if (r == -248 || r == -241) continue;             // prechodne
                int cl = Math.Min((int)resp.codeLen, resp.code?.Length ?? 0);
                var epc = resp.code?.Take(cl).ToArray() ?? Array.Empty<byte>();
                if (expectEpc != null && epc.Length > 0 && !TagDecode.SameBytes(epc, expectEpc)) continue;
                if (r == 0) { ok = true; status = 0; }
                else if (!ok) status = r;
            }
            if (!terminated) { try { NativeApi.InventoryStop(_h, 1000); } catch { } }
            return new OpResult { Ok = ok, Status = status, Message = ok ? okMsg : ErrorCodes.Text(status) };
        }

        public OpResult WriteMemory(Bank bank, ushort wordPtr, byte[] data, byte[] accPwd = null, byte[] expectEpc = null)
        {
            accPwd ??= new byte[4];
            if (data == null || data.Length == 0 || data.Length % 2 != 0)
                return new OpResult { Ok = false, Message = "Data musi byt po celych slovech (2 B)." };
            byte words = (byte)(data.Length / 2);
            int res = NativeApi.WriteTag(_h, 0, accPwd, (byte)bank, wordPtr, words, data);
            return CollectTagResp(CMD_WRITE, res, expectEpc, "Zapis OK");
        }

        // Zapis noveho EPC. newEpc = cele EPC v bajtech (nasobek 2).
        // PC slovo: zmeni se JEN delka (horni 5 bitu); UMI/XI/T/AFI-NSI se zachovaji z aktualniho PC.
        // currentEpc = EPC tagu, na ktery se zapisuje (pro kontrolu odpovedi; select nastavi volajici).
        public OpResult WriteEpc(byte[] newEpc, byte[] accPwd = null, byte[] currentEpc = null)
        {
            if (newEpc == null || newEpc.Length == 0 || newEpc.Length % 2 != 0)
                return new OpResult { Ok = false, Message = "EPC musi byt v celych slovech (2 B)." };
            int words = newEpc.Length / 2;
            if (words > 31) return new OpResult { Ok = false, Message = "EPC je prilis dlouhe (max 31 slov)." };

            var cur = ReadMemory(Bank.Epc, 1, 1, accPwd, 2, currentEpc);     // aktualni PC
            if (!cur.Ok || cur.Data.Length < 2)
                return new OpResult { Ok = false, Status = cur.Status, Message = "Nelze precist aktualni PC slovo (" + cur.StatusText + ") – zapis zrusen." };
            int oldPc = (cur.Data[0] << 8) | cur.Data[1];
            int pc = ((words & 0x1F) << 11) | (oldPc & 0x07FF);

            var buf = new byte[2 + newEpc.Length];
            buf[0] = (byte)(pc >> 8); buf[1] = (byte)(pc & 0xFF);
            Array.Copy(newEpc, 0, buf, 2, newEpc.Length);
            // zapis od slova 1 (PC), nasleduje EPC
            var w = WriteMemory(Bank.Epc, 1, buf, accPwd, currentEpc);
            if (w.Ok) w.Message = string.Format("Zapis EPC OK (PC {0:X4} -> {1:X4})", oldPc, pc);
            return w;
        }

        public OpResult LockBank(LockArea area, LockAction action, byte[] accPwd = null)
        {
            accPwd ??= new byte[4];
            int res = NativeApi.LockTag(_h, accPwd, (byte)area, (byte)action);
            return CollectTagResp(CMD_LOCK, res, null, "Lock OK");
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
            return CollectTagResp(CMD_KILL, res, null, "Tag zabit (kill OK)");
        }

        // ---------------- OBNOVA HESLA (vlastni tagy) ----------------
        // NEDESTRUKTIVNI orakulum CTENIM (overeno na CF601 + Impinj M750):
        //   pri nenulovem hesle ctecka pred ctenim posle Gen2 Access. Spatne heslo -> cteni
        //   TID selze (-247); spravne heslo -> cteni projde. Do tagu se NIC nezapisuje.
        // Funguje na kazdem tagu s nastavenym (nenulovym) access heslem. Nalezene heslo se
        // potvrdi prectenim RESERVED banky, ktera po odemceni ukaze kill + access heslo.

        public byte[] RecoverAccessPassword(IEnumerable<byte[]> candidates, byte[] selectEpc,
                                            Action<string> progress, Func<bool> cancel = null, long total = 0)
        {
            void P(string s) => progress?.Invoke(s);
            if (selectEpc != null) SelectByEpc(selectEpc);
            try
            {
                // 1) tag musi odpovidat bez hesla (jinak by kazdy pokus vypadal jako "spatne heslo")
                var ctl = ReadMemory(Bank.Tid, 0, 2, null, 2, selectEpc);
                if (!ctl.Ok) { P("Tag neodpovida (kontrolni cteni TID: " + ctl.StatusText + "). Priloz tag a zkus znovu."); return null; }

                // 2) neni RESERVED citelna rovnou? pak heslo vycteme primo
                var direct = ReadMemory(Bank.Reserved, 2, 2, null, 1, selectEpc);
                if (direct.Ok && direct.Data.Length >= 4)
                {
                    bool zero = direct.Data.All(b => b == 0);
                    P("RESERVED je citelna bez hesla – access heslo = " + TagDecode.Hex(direct.Data, false) +
                      (zero ? " (NENASTAVENO, tag zadne heslo nema)" : " (vycteno primo)"));
                    return zero ? null : direct.Data.Take(4).ToArray();
                }

                long tried = 0; int ctlFails = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var cand in candidates)
                {
                    if (cancel != null && cancel()) { P(string.Format("Preruseno po {0} pokusech.", tried)); return null; }
                    if (cand == null || cand.Length != 4 || cand.All(b => b == 0)) continue; // 0 = bez Access, nic neoveri
                    tried++;

                    var t = ReadMemory(Bank.Tid, 0, 2, cand, 0, selectEpc);
                    if (t.Ok)
                    {
                        // potvrzeni: s timto heslem musi jit precist RESERVED (kill + access)
                        var conf = ReadMemory(Bank.Reserved, 0, 4, cand, 2, selectEpc);
                        if (conf.Ok && conf.Data.Length >= 8)
                            P(string.Format("NALEZENO po {0} pokusech: {1}   (RESERVED: kill {2}, access {3})", tried,
                                TagDecode.Hex(cand, false), TagDecode.Hex(conf.Data.Take(4).ToArray(), false),
                                TagDecode.Hex(conf.Data.Skip(4).Take(4).ToArray(), false)));
                        else
                            P(string.Format("NALEZENO po {0} pokusech: {1}   (cteni s heslem proslo; RESERVED: {2})",
                                tried, TagDecode.Hex(cand, false), conf.StatusText));
                        return cand;
                    }

                    // prubezna kontrola, ze tag porad lezi na ctecce
                    if (tried % 50 == 0)
                    {
                        if (!ReadMemory(Bank.Tid, 0, 2, null, 2, selectEpc).Ok)
                        {
                            if (++ctlFails >= 2) { P("Tag prestal odpovidat (posunut / odebran?) – obnova prerusena po " + tried + " pokusech."); return null; }
                        }
                        else ctlFails = 0;
                    }
                    if (tried % 100 == 0)
                    {
                        double rate = tried / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
                        string eta = total > 0 && rate > 0 ? "  zbyva ~" + FmtDur((total - tried) / rate) : "";
                        P(string.Format("{0}{1} pokusu   {2:0.0} /s{3}", tried, total > 0 ? "/" + total : "", rate, eta));
                    }
                }
                P(string.Format("Heslo nenalezeno ({0} kandidatu).", tried));
                return null;
            }
            finally { ClearSelect(); }
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
                // "0x12345678" – prefix 0x jinak prida nulu navic (9 znaku) a heslo by se zahodilo
                var t = tok.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? tok.Substring(2) : tok;
                var h = new string(t.Where(Uri.IsHexDigit).ToArray());
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

        // Test citlivosti: ramp vykonu SHORA DOLU, pro kazdy vykon kratka inventura cile.
        // Konci po 2 neuspesnych krocich za sebou: krok bez cteni trva na CF601 ~11-17 s
        // (InventoryContinue bez tagu v dosahu vraci timeouty), krok se ctenim ~1 s.
        // Vysledek (minimalni vykon) je stejny jako u plneho rampu, jen rychleji.
        public List<(int power, double readRate, double rssi)> SensitivityTest(
            byte[] targetEpc, int minPow, int maxPow, double perStep, Action<string> progress, Func<bool> cancel = null)
        {
            var res = new List<(int, double, double)>();
            // Puvodni vykon z DevicePara.RFIDPOWER (overeno). NE z GetRFPower – ta na CF601 vratila 1,
            // takze se vykon po testu "obnovil" na 1 dBm a ctecka pak tag nevidela.
            if (GetParaRetry(out var p0, 10) != 0)
            {
                progress?.Invoke("Nelze zjistit aktualni vykon – test zrusen (vykon by se pak nedal vratit).");
                return res;
            }
            byte origPow = p0.RFIDPOWER;
            try
            {
                int failsInRow = 0;
                for (int pow = maxPow; pow >= minPow; pow--)
                {
                    if (cancel != null && cancel()) break;
                    if (failsInRow >= 2)
                    {
                        progress?.Invoke(string.Format("   (pod {0} dBm uz nemereno – 2 kroky za sebou bez cteni)", pow + 1));
                        break;
                    }
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    int sp = NativeApi.SetRFPower(_h, (byte)pow, 0);
                    // Po SetRFPower je ctecka chvili zaneprazdnena (prikazy vraci -238).
                    // Pockej, az zase odpovida – jinak by inventura falesne hlasila 0 %.
                    bool ready = GetParaRetry(out _, 20) == 0;
                    var found = Inventory(perStep, out int polls, 6);
                    TagRecord t = targetEpc != null
                        ? found.Values.FirstOrDefault(x => TagDecode.SameBytes(x.Epc, targetEpc))
                        : found.Values.OrderByDescending(x => x.RssiAvg).FirstOrDefault();
                    double rate = t != null ? 100.0 * t.Count / Math.Max(polls, 1) : 0;
                    double rssi = t?.RssiAvg ?? 0;
                    res.Add((pow, rate, rssi));
                    failsInRow = rate > 0 ? 0 : failsInRow + 1;
                    progress?.Invoke(string.Format("{0,2} dBm: cteni {1,3:0} %  RSSI {2}{3}   ({4} ms)", pow, rate,
                        t != null ? rssi.ToString("0.0") : "-",
                        sp != 0 ? "  [SetRFPower " + sp + "]" : !ready ? "  [ctecka neodpovidala]" : "",
                        sw.ElapsedMilliseconds));
                }
            }
            finally
            {
                // vratit puvodni vykon a OVERIT zpetnym ctenim (az 3 pokusy)
                bool restored = false;
                for (int i = 0; i < 3 && !restored; i++)
                {
                    NativeApi.SetRFPower(_h, origPow, 0);
                    restored = GetParaRetry(out var p1, 20) == 0 && p1.RFIDPOWER == origPow;
                }
                progress?.Invoke(restored
                    ? "Vykon vracen na " + origPow + " dBm."
                    : "!! Vykon se NEPODARILO vratit na " + origPow + " dBm – nastav ho v zalozce Konfigurace.");
            }
            return res;
        }

        // Soak test stability
        public SoakStat SoakTest(double seconds, Action<string> progress, Func<bool> cancel = null)
        {
            var rssis = new List<double>();
            var uniq = new HashSet<string>();
            int reads = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // Soak bezi celou dobu: kdyz inventura skonci (-249 = tag chvili nebyl videt), spust ji znovu.
            int polls = 0;
            while (sw.Elapsed.TotalSeconds < seconds && !(cancel != null && cancel()))
            {
                double left = seconds - sw.Elapsed.TotalSeconds;
                bool started = InventoryLoop(left, 0, tag =>
                {
                    reads++;
                    rssis.Add(tag.rssi / 10.0);
                    int n = Math.Min((int)tag.codeLen, tag.code.Length);
                    var epc = new byte[n]; Array.Copy(tag.code, epc, n);
                    uniq.Add(TagDecode.Hex(epc, false));
                    if (reads % 100 == 0) progress?.Invoke(reads + " cteni...");
                }, cancel, out int p);
                polls += p;
                if (!started) { progress?.Invoke("Inventuru se nepodarilo spustit (ctecka neodpovida)."); break; }
            }
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
            // Znamka podle PODILU uspesnych odezev, ne podle absolutni rychlosti: CF601 zvladne
            // jen ~22 cteni/s i pri dokonalem signalu (puvodni hranice A >150/s byly nedosazitelne).
            st.SuccessPct = 100.0 * reads / Math.Max(st.Polls, 1);
            st.Grade = st.SuccessPct >= 95 ? "A (vyborne)" : st.SuccessPct >= 80 ? "B (dobre)"
                     : st.SuccessPct >= 50 ? "C (prumerne)" : st.SuccessPct >= 20 ? "D (slabe)" : "F (nefunkcni)";
            return st;
        }

        // ---------------- KONFIGURACE ----------------

        public ReaderConfig GetFullConfig()
        {
            var c = new ReaderConfig();
            if (NativeApi.GetDevicePara(_h, out var p) == 0)
            {
                c.Raw = p; c.Region = p.REGION; c.Power = p.RFIDPOWER; c.QValue = p.QVALUE;
                c.Session = p.SESSION; c.Workmode = p.WORKMODE; c.Antenna = p.ANT; c.Port = p.INTERFACE;
                // big-endian: STRATFREI = cele MHz, STRATFRED = desetinna cast v kHz, STEPFRE = krok v kHz
                int mhz = (p.STRATFREI[0] << 8) | p.STRATFREI[1];
                int khz = (p.STRATFRED[0] << 8) | p.STRATFRED[1];
                c.FreqStepKHz = (p.STEPFRE[0] << 8) | p.STEPFRE[1];
                c.FreqChannels = p.CN;
                c.FreqStartMHz = mhz + khz / 1000.0;
                c.FreqEndMHz = c.FreqStartMHz + Math.Max(0, p.CN - 1) * c.FreqStepKHz / 1000.0;
            }
            if (NativeApi.GetRFIDType(_h, out var t) == 0) c.RfidType = t;
            if (NativeApi.GetTemperature(_h, out var cur, out var lim) == 0) { c.TempCur = cur; c.TempLimit = lim; }
            var info = GetDeviceInfo();   // sjednoceno: FW/HW/SN cele ctecky (viz Reader.GetDeviceInfo)
            c.Firmware = info.Firmware; c.Hardware = info.Hardware; c.Sn = info.Sn;
            return c;
        }

        // Nastaveni zakladnich parametru (vykon/Q/session/workmode) pres DevicePara.
        // Region se tu NEMENI – na to je SetRegion (musi nastavit i frekvencni tabulku).
        public OpResult SetBasicConfig(int power, int qvalue, int session, int workmode)
        {
            if (NativeApi.GetDevicePara(_h, out var p) != 0)
                return new OpResult { Ok = false, Message = "Nelze nacist aktualni parametry." };
            p.RFIDPOWER = (byte)power; p.QVALUE = (byte)qvalue;
            p.SESSION = (byte)session; p.WORKMODE = (byte)workmode;
            int res = NativeApi.SetDevicePara(_h, p);
            return new OpResult { Ok = res == 0, Status = res, Message = res == 0 ? "Konfigurace ulozena" : ErrorCodes.Text(res) };
        }

        // Prepne radiovy region VCETNE frekvencni tabulky (stejne jako vyrobcova aplikace):
        // REGION + STRATFREI (cele MHz) + STRATFRED (kHz) + STEPFRE (kHz) + CN (pocet kanalu).
        public OpResult SetRegion(int code)
        {
            var ri = TagDecode.Region(code);
            if (ri == null) return new OpResult { Ok = false, Message = "Neznamy region " + code + "." };
            if (NativeApi.GetDevicePara(_h, out var p) != 0)
                return new OpResult { Ok = false, Message = "Nelze nacist aktualni parametry." };
            int mhz = (int)Math.Floor(ri.StartMHz + 1e-9);
            int khz = (int)Math.Round((ri.StartMHz - mhz) * 1000);
            p.REGION = (byte)code;
            p.STRATFREI = new[] { (byte)(mhz >> 8), (byte)mhz };
            p.STRATFRED = new[] { (byte)(khz >> 8), (byte)khz };
            p.STEPFRE = new[] { (byte)(ri.StepKHz >> 8), (byte)ri.StepKHz };
            p.CN = (byte)ri.Channels;
            int res = NativeApi.SetDevicePara(_h, p);
            if (res != 0) return new OpResult { Ok = false, Status = res, Message = "SetDevicePara: " + ErrorCodes.Text(res) };
            Thread.Sleep(150);
            var c = GetFullConfig();
            bool ok = c.Region == code && Math.Abs(c.FreqStartMHz - ri.StartMHz) < 0.0005
                      && c.FreqStepKHz == ri.StepKHz && c.FreqChannels == ri.Channels;
            return new OpResult
            {
                Ok = ok, Status = res,
                Message = ok ? "Region nastaven: " + ri
                             : string.Format("Region zapsan, ale overeni nesedi (ctecka hlasi region {0}, {1:0.000} MHz, krok {2} kHz, {3} kan.)",
                                             c.Region, c.FreqStartMHz, c.FreqStepKHz, c.FreqChannels)
            };
        }


        // ---------------- DUMP PAMETI ----------------
        public class BankDump { public Bank Bank; public byte[] Data; public bool Ok; public string Note; }

        public List<BankDump> FullMemoryDump(byte[] accPwd = null, byte[] selectEpc = null)
        {
            accPwd ??= new byte[4];
            if (selectEpc != null) SelectByEpc(selectEpc);
            var res = new List<BankDump>();
            try
            {
                // RESERVED: 4 slova (kill + access); ostatni banky az do konce (velikost se zjisti cteni)
                res.Add(DumpBank(ReadMemory(Bank.Reserved, 0, 4, accPwd, expectEpc: selectEpc), Bank.Reserved));
                res.Add(DumpBank(ReadBankAll(Bank.Epc, 34, accPwd, selectEpc), Bank.Epc));     // CRC+PC+az 31 slov EPC (+XPC)
                res.Add(DumpBank(ReadBankAll(Bank.Tid, 32, accPwd, selectEpc), Bank.Tid));
                res.Add(DumpBank(ReadBankAll(Bank.User, 128, accPwd, selectEpc), Bank.User));
            }
            finally { ClearSelect(); }
            return res;
        }

        static BankDump DumpBank(BankRead r, Bank b) =>
            new BankDump { Bank = b, Data = r.Data, Ok = r.Ok && r.Data.Length > 0, Note = r.Ok ? null : r.StatusText };
    }
}
