using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UhfTagAnalyzer
{
    // Radiovy region ctecky: kod REGION + frekvencni plan (start, krok, pocet kanalu).
    public sealed class RegionInfo
    {
        public int Code { get; }
        public string Name { get; }
        public double StartMHz { get; }
        public int StepKHz { get; }
        public int Channels { get; }
        public double EndMHz => StartMHz + (Channels - 1) * StepKHz / 1000.0;

        public RegionInfo(int code, string name, double startMHz, int stepKHz, int channels)
        { Code = code; Name = name; StartMHz = startMHz; StepKHz = stepKHz; Channels = channels; }

        public override string ToString() =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} ({1:0.0##}–{2:0.0##} MHz)", Name, StartMHz, EndMHz);
    }

    // Model cipu z tabulky TMN (overeno v datasheetu vyrobce)
    public sealed class ChipModel
    {
        public string Name { get; }
        public string Memory { get; }   // velikost pameti dle datasheetu (muze byt null)
        public string Note { get; }     // poznamka menici vyklad analyzy (muze byt null)
        public ChipModel(string name, string memory = null, string note = null) { Name = name; Memory = memory; Note = note; }
    }

    public class PcInfo
    {
        public string PcHex;
        public int EpcWords;
        public int EpcBits;
        public bool Umi;      // User Memory Indicator – tag ma data v USER
        public bool Xpc;      // rozsirene PC pritomno
        public int Toggle;    // 0 = GS1/EPC, 1 = ISO
        public string Scheme;
        public int NsiAfi;
    }

    public class TidInfo
    {
        public byte[] RawBytes;
        public string Raw;
        public string ClassHex;
        public string ClassDesc;
        public string Vendor;
        public string Mdid;
        public string Model;
        public string ModelMemory;   // pamet dle datasheetu (je-li znama)
        public string ModelNote;     // poznamka k cipu (napr. sdilene heslo)
        public string Tmn;
        public bool Xtid;
        public bool Serialized;
        public string Summary;
    }

    public static class TagDecode
    {
        // Vyrobci dle 9bitoveho MDID – registr GS1 "Mask Designer ID Assignment"
        // (prepis registru v knihovne gs1/epc/tid.rs; 0x001/003/006/007/00B krizove overeny
        // GoToTags + datasheety Impinj, NXP, Alien). Overeno 2026-11-11.
        static readonly Dictionary<int, string> Vendors = new Dictionary<int, string>
        {
            {0x001,"Impinj"}, {0x002,"Texas Instruments"}, {0x003,"Alien Technology"},
            {0x004,"Intelleflex"}, {0x005,"Atmel"}, {0x006,"NXP Semiconductors"},
            {0x007,"STMicroelectronics"}, {0x008,"EP Microelectronics"},
            {0x009,"Motorola (Symbol Technologies)"}, {0x00A,"Sentech Snd Bhd"},
            {0x00B,"EM Microelectronic"}, {0x00C,"Renesas Technology"}, {0x00D,"Mstar"},
            {0x00E,"Tyco International"}, {0x00F,"Quanray Electronics"}, {0x010,"Fujitsu"},
            {0x011,"LSIS"}, {0x012,"CAEN RFID"}, {0x013,"Productivity Engineering"},
            {0x014,"Federal Electric"}, {0x015,"ON Semiconductor"}, {0x016,"Ramtron"},
            {0x017,"Tego"}, {0x018,"Ceitec"}, {0x019,"CPA Wernher von Braun"},
            {0x01A,"TransCore"}, {0x01B,"Nationz"}, {0x01C,"Invengo"}, {0x01D,"Kiloway"},
            {0x01E,"Longjing Microelectronics"}, {0x01F,"Chipus Microelectronics"},
            {0x020,"ORIDAO"}, {0x021,"Maintag"}, {0x022,"Yangzhou Daoyuan Microelectronics"},
            {0x023,"Gate Elektronik"}, {0x024,"RFMicron (Axzon)"}, {0x025,"RST-Invent"},
            {0x026,"Crystone Technology"}, {0x027,"Shanghai Fudan Microelectronics"},
            {0x028,"Farsens"}, {0x029,"Giesecke & Devrient"}, {0x02A,"AWID"},
            {0x02B,"Unitec Semicondutores"}, {0x02C,"Q-Free"}, {0x02D,"Valid S.A."},
            {0x02E,"Fraunhofer IPMS"}, {0x02F,"ams AG"}, {0x030,"Angstrem"},
            {0x031,"Honeywell"}, {0x032,"Huada Semiconductor (HDSC)"}, {0x033,"Lapis Semiconductor"},
            {0x034,"PJSC Mikron"}, {0x035,"Hangzhou Landa Microelectronics"},
            {0x036,"Nanjing NARI Micro-Electronic"}, {0x037,"Southwest Integrated Circuit Design"},
            {0x038,"Silictec"}, {0x039,"Nation RFID"}, {0x03A,"Asygn"},
            {0x03B,"Suzhou HCTech Technology"}, {0x03C,"AXEM Technology"},
        };

        // Modely dle (MDID, TMN) – JEN hodnoty overene v datasheetech vyrobcu (2026-11-11).
        // TMN = 12 bitu z TID bitu 14h-1Fh (u NXP vcetne "config word indicator").
        // Neznamy TMN se vypise jako "model TMN 0x... – neni v tabulce" (vzdy i se surovym TID).
        static readonly Dictionary<(int, int), ChipModel> Models = new Dictionary<(int, int), ChipModel>
        {
            // Impinj – Monza 4 datasheet (2015) Tab. 4-7
            {(0x001,0x100), new ChipModel("Monza 4D")},
            {(0x001,0x105), new ChipModel("Monza 4QT")},
            {(0x001,0x10C), new ChipModel("Monza 4E")},
            // Impinj – Monza R6 datasheet v7.0 Tab. 14 / Monza R6-P datasheet v7.0 Tab. 16
            {(0x001,0x160), new ChipModel("Monza R6", "EPC 96 bit, USER 0 bit",
                "Monza R6 hesla nema – RESERVED banka se chova jako trvale zamcena. Neuspech cteni RESERVED tedy NEZNAMENA nastavene heslo.")},
            {(0x001,0x170), new ChipModel("Monza R6-P")},
            // Impinj – M700 series datasheet v6.4 Tab. 1 a 11-13
            {(0x001,0x190), new ChipModel("M750", "EPC 96 bit, USER 32 bit", M700Pwd)},
            {(0x001,0x191), new ChipModel("M730", "EPC 128 bit, USER 0 bit", M700Pwd)},
            {(0x001,0x1A0), new ChipModel("M770", "EPC 128 bit, USER 32 bit", M700Pwd)},
            // NXP – SL3S1204 UCODE 7 (Rev. 3.5 = 810h, Rev. 4.0 = 890h)
            {(0x006,0x810), new ChipModel("UCODE 7")},
            {(0x006,0x890), new ChipModel("UCODE 7")},
            // NXP – SL3S1205_15 UCODE 8/8m Rev. 3.2, kap. 9.5.2
            {(0x006,0x894), new ChipModel("UCODE 8")},
            {(0x006,0x994), new ChipModel("UCODE 8m")},
            // NXP – SL3S1206 UCODE 9 Rev. 3.5 (dve varianty TID)
            {(0x006,0x995), new ChipModel("UCODE 9")},
            {(0x006,0x915), new ChipModel("UCODE 9")},
            // NXP – SL3S1005 UCODE 9xm Rev. 3.1 / SL3S1216 UCODE 9xe Rev. 3.3
            {(0x006,0x897), new ChipModel("UCODE 9xm")},
            {(0x006,0xA16), new ChipModel("UCODE 9xe")},
            {(0x006,0xA96), new ChipModel("UCODE 9xe")},
            // Alien – Higgs 3 / Higgs 4 IC datasheet (TID E200 3412 / E200 3414)
            {(0x003,0x412), new ChipModel("Higgs-3")},
            {(0x003,0x414), new ChipModel("Higgs-4")},
        };

        const string M700Pwd = "Impinj M700 ma jedno sdilene 32bit heslo: kill a access heslo jsou totez " +
                               "(zmena jednoho zmeni i druhe).";

        // Regiony dle vyrobce (SDK ChannelRegion.cs: enum + ChannelRegionItem). Kod = bajt REGION
        // v DevicePara. Zmena regionu vyzaduje nastavit i frekvencni tabulku (start, krok, pocet).
        // Overeno na CF601: REGION=1 + 902.750 MHz / 500 kHz / 50 kanalu = USA.
        public static readonly RegionInfo[] RegionTable =
        {
            new RegionInfo(6, "EU / ETSI – 4 kanaly",   865.700, 600, 4),
            new RegionInfo(3, "EU – 15 kanalu",         865.100, 200, 15),
            new RegionInfo(1, "USA / FCC",              902.750, 500, 50),
            new RegionInfo(2, "Korea",                  917.100, 200, 32),
            new RegionInfo(4, "Japonsko",               952.200, 200, 8),
            new RegionInfo(5, "Malajsie",               919.500, 500, 7),
            new RegionInfo(7, "Cina 1",                 840.125, 250, 20),
            new RegionInfo(8, "Cina 2",                 920.125, 250, 20),
        };

        public static RegionInfo Region(int code) => RegionTable.FirstOrDefault(r => r.Code == code);

        public static string RegionName(int code) =>
            Region(code)?.ToString() ?? (code == 0 ? "Vlastni frekvence" : "? (kod " + code + ")");

        // V EU je pro UHF RFID povolene jen pasmo 865–868 MHz (ETSI EN 302 208).
        public static bool IsEuRegion(int code) => code == 3 || code == 6;

        public static string Hex(byte[] data, bool spaced = true)
        {
            if (data == null || data.Length == 0) return "(prazdne)";
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                sb.Append(data[i].ToString("X2"));
                if (spaced && i < data.Length - 1) sb.Append(' ');
            }
            return sb.ToString();
        }

        public static PcInfo DecodePc(byte[] pcBytes)
        {
            if (pcBytes == null || pcBytes.Length < 2) return null;
            int pc = (pcBytes[0] << 8) | pcBytes[1];
            int words = (pc >> 11) & 0x1F;
            int toggle = (pc >> 8) & 0x01;
            return new PcInfo
            {
                PcHex = pc.ToString("X4"),
                EpcWords = words,
                EpcBits = words * 16,
                Umi = ((pc >> 10) & 1) != 0,
                Xpc = ((pc >> 9) & 1) != 0,
                Toggle = toggle,
                Scheme = toggle == 0 ? "GS1 / EPC" : "ISO (non-GS1)",
                NsiAfi = pc & 0xFF,
            };
        }

        public static TidInfo DecodeTid(byte[] tid)
        {
            var o = new TidInfo { Raw = Hex(tid), RawBytes = tid };
            if (tid == null || tid.Length < 2) { o.Summary = "TID prilis kratke / necitelne"; return o; }

            int cls = tid[0];
            o.ClassHex = cls.ToString("X2");
            o.ClassDesc = cls == 0xE2 ? "EPCglobal Gen2 / ISO 18000-6C"
                        : cls == 0xE0 ? "ISO 14443 / jine (class E0)"
                        : cls == 0xE3 ? "ISO 18000-6B"
                        : string.Format("neznama trida (0x{0:X2})", cls);

            if (tid.Length < 4 || cls != 0xE2) { o.Summary = o.ClassDesc; return o; }

            int field12 = ((tid[1] << 8) | tid[2]) >> 4; // 12 bitu za E2
            int mdid = field12 & 0x1FF;                   // 9bitovy registrovany MDID
            int tmn = ((tid[2] & 0x0F) << 8) | tid[3];    // 12bit cislo modelu
            o.Xtid = ((field12 >> 11) & 1) != 0;
            o.Mdid = "0x" + mdid.ToString("X3");
            o.Tmn = "0x" + tmn.ToString("X3");
            o.Vendor = Vendors.TryGetValue(mdid, out var v) ? v : "neznamy vyrobce";
            if (Models.TryGetValue((mdid, tmn), out var m))
            {
                o.Model = m.Name; o.ModelMemory = m.Memory; o.ModelNote = m.Note;
            }

            // s XTID: serializace je v XTID hlavicce; bez XTID jen heuristika (nenulova data za TMN)
            o.Serialized = o.Xtid && tid.Length >= 6
                ? ((((tid[4] << 8) | tid[5]) >> 13) & 0x7) != 0
                : tid.Length >= 8 && HasNonZero(tid, 4);
            o.Summary = o.Model != null
                ? string.Format("{0} {1}", o.Vendor, o.Model)
                : string.Format("{0} (model TMN {1} – neni v tabulce)", o.Vendor, o.Tmn);
            return o;
        }

        static bool HasNonZero(byte[] a, int from)
        {
            for (int i = from; i < a.Length; i++) if (a[i] != 0) return true;
            return false;
        }

        // CRC-16/CCITT (poly 0x1021) tabulka
        static readonly ushort[] Crc16 = BuildCrc();
        static ushort[] BuildCrc()
        {
            var t = new ushort[256];
            for (int i = 0; i < 256; i++)
            {
                int c = i << 8;
                for (int j = 0; j < 8; j++)
                    c = ((c & 0x8000) != 0) ? ((c << 1) ^ 0x1021) : (c << 1);
                t[i] = (ushort)(c & 0xFFFF);
            }
            return t;
        }

        // Gen2 EPC CRC-16 nad PC+EPC (init 0xFFFF, na konci negace)
        public static byte[] Gen2Crc16(byte[] pc, byte[] epc)
        {
            int crc = 0xFFFF;
            void Feed(byte[] d) { foreach (var b in d) crc = ((crc << 8) ^ Crc16[((crc >> 8) ^ b) & 0xFF]) & 0xFFFF; }
            Feed(pc); Feed(epc);
            crc ^= 0xFFFF;
            return new[] { (byte)((crc >> 8) & 0xFF), (byte)(crc & 0xFF) };
        }

        public static string AsciiPreview(byte[] data)
        {
            var sb = new StringBuilder();
            foreach (var b in data) sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            return sb.ToString();
        }

        public static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ---------------- EPC obsah (GS1 EPC Tag Data Standard) ----------------
        static readonly Dictionary<byte, string> EpcHeader = new Dictionary<byte, string>
        {
            // GS1 EPC Tag Data Standard – EPC header
            {0x2C,"GDTI-96 (dokument)"}, {0x2D,"GSRN-96 (sluzebni vztah)"}, {0x2E,"GSRNP-96"},
            {0x2F,"USDoD-96 (armada USA)"},
            {0x30,"SGTIN-96 (produkt/GTIN)"}, {0x31,"SSCC-96 (logisticka jednotka)"},
            {0x32,"SGLN-96 (lokace/GLN)"}, {0x33,"GRAI-96 (vratny obal)"},
            {0x34,"GIAI-96 (majetek)"}, {0x35,"GID-96 (obecne)"}, {0x36,"SGTIN-198"},
            {0x37,"GRAI-170"}, {0x38,"GIAI-202"}, {0x39,"SGLN-195"}, {0x3A,"GDTI-113"},
            {0x3B,"ADI-var (letectvi)"}, {0x3C,"CPI-96"}, {0x3D,"CPI-var"},
        };

        // bitovy ctecka pro EPC
        static long Bits(byte[] d, int start, int len)
        {
            long v = 0;
            for (int i = 0; i < len; i++)
            {
                int bit = start + i;
                int b = (d[bit >> 3] >> (7 - (bit & 7))) & 1;
                v = (v << 1) | (uint)b;
            }
            return v;
        }

        // partition tabulka SGTIN-96: partition -> (bity CP, cislice CP, bity item, cislice item)
        static readonly (int cpBits, int cpDig, int irBits, int irDig)[] SgtinPart =
        {
            (40,12,4,1),(37,11,7,2),(34,10,10,3),(30,9,14,4),(27,8,17,5),(24,7,20,6),(20,6,24,7)
        };
        static readonly (int cpBits, int cpDig, int srBits, int srDig)[] SsccPart =
        {
            (40,12,18,5),(37,11,21,6),(34,10,24,7),(30,9,28,8),(27,8,31,9),(24,7,34,10),(20,6,38,11)
        };

        public static string InterpretEpc(byte[] epc)
        {
            if (epc == null || epc.Length == 0) return "(prazdne EPC)";
            byte hdr = epc[0];
            string name = EpcHeader.TryGetValue(hdr, out var s) ? s : null;
            if (name == null)
                return string.Format("Header 0x{0:X2} – neni standardni GS1 schema (muze byt vlastni/surove ID)", hdr);

            var sb = new StringBuilder();
            sb.Append("Schema: " + name);

            try
            {
                if (hdr == 0x30 && epc.Length >= 12) // SGTIN-96
                {
                    int filter = (int)Bits(epc, 8, 3);
                    int part = (int)Bits(epc, 11, 3);
                    if (part <= 6)
                    {
                        var p = SgtinPart[part];
                        long cp = Bits(epc, 14, p.cpBits);
                        long ir = Bits(epc, 14 + p.cpBits, p.irBits);
                        long serial = Bits(epc, 58, 38);
                        string cpS = cp.ToString().PadLeft(p.cpDig, '0');
                        string irS = ir.ToString().PadLeft(p.irDig, '0');
                        string gtin = BuildGtin14(cpS, irS);
                        sb.Append(string.Format("\n  Filter: {0}   Company prefix: {1}   Item ref: {2}", filter, cpS, irS));
                        sb.Append(string.Format("\n  GTIN-14: {0}   Serial: {1}", gtin, serial));
                    }
                }
                else if (hdr == 0x31 && epc.Length >= 12) // SSCC-96
                {
                    int filter = (int)Bits(epc, 8, 3);
                    int part = (int)Bits(epc, 11, 3);
                    if (part <= 6)
                    {
                        var p = SsccPart[part];
                        long cp = Bits(epc, 14, p.cpBits);
                        long sr = Bits(epc, 14 + p.cpBits, p.srBits);
                        sb.Append(string.Format("\n  Filter: {0}   Company prefix: {1}   Serial ref: {2}",
                            filter, cp.ToString().PadLeft(p.cpDig, '0'), sr.ToString().PadLeft(p.srDig, '0')));
                    }
                }
                else if (hdr == 0x35 && epc.Length >= 12) // GID-96
                {
                    long mgr = Bits(epc, 8, 28);
                    long cls = Bits(epc, 36, 24);
                    long serial = Bits(epc, 60, 36);
                    sb.Append(string.Format("\n  Manager: {0}   Class: {1}   Serial: {2}", mgr, cls, serial));
                }
            }
            catch { /* best-effort dekod */ }
            return sb.ToString();
        }

        // SGTIN company prefix + item ref -> GTIN-14 (item ref prvni cislice = indikator)
        static string BuildGtin14(string cp, string itemRef)
        {
            if (itemRef.Length < 1) return "?";
            string indicator = itemRef.Substring(0, 1);
            string rest = itemRef.Substring(1);
            string d13 = indicator + cp + rest;
            if (d13.Length != 13) return "?";
            int sum = 0;
            for (int i = 0; i < 13; i++)
            {
                int d = d13[i] - '0';
                sum += (i % 2 == 0) ? d * 3 : d;
            }
            int check = (10 - (sum % 10)) % 10;
            return d13 + check;
        }

        // XTID hlavicka = 16 bitu na TID bitove adrese 20h (= bajty 4-5, hned za class/MDID/TMN).
        // Horni 3 bity = serializace N: delka serioveho cisla = 48 + 16*(N-1) bitu (0 = zadne).
        // Overeno: Impinj M730 TID E2801190 2000 ... (96 bit) -> 0x2000 = N=1 = 48bit serial.
        // Ostatni bity (priznaky volitelnych segmentu) zamerne nedekodujeme – poradi neni overene;
        // vypisuje se syrova hodnota.
        public static string ParseXtid(byte[] tid)
        {
            if (tid == null || tid.Length < 6) return null;
            int xtid = (tid[4] << 8) | tid[5];
            int n = (xtid >> 13) & 0x7;
            int serialBits = n == 0 ? 0 : 48 + 16 * (n - 1);
            var sb = new StringBuilder();
            sb.Append(string.Format("XTID hlavicka: 0x{0:X4}", xtid));
            sb.Append(string.Format("\n  Seriove cislo cipu: {0}", serialBits > 0 ? serialBits + " bit" : "ne"));
            if (serialBits > 0 && tid.Length >= 6 + serialBits / 8)
                sb.Append("  = " + Hex(tid.Skip(6).Take(serialBits / 8).ToArray(), false));
            return sb.ToString();
        }
    }
}
