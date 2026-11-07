using System;
using System.Collections.Generic;
using System.Text;

namespace UhfTagAnalyzer
{
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
        public string Tmn;
        public bool Xtid;
        public bool Serialized;
        public string Summary;
    }

    public static class TagDecode
    {
        // Vyrobci dle 9bitoveho MDID (registr GS1 / EPCglobal)
        static readonly Dictionary<int, string> Vendors = new Dictionary<int, string>
        {
            {0x001,"Impinj"}, {0x002,"Texas Instruments"}, {0x003,"Alien Technology"},
            {0x004,"Intelleflex"}, {0x005,"Atmel / Microchip"}, {0x006,"NXP Semiconductors"},
            {0x007,"STMicroelectronics"}, {0x008,"EM Microelectronic"}, {0x009,"Motorola / Symbol"},
            {0x00A,"Sentech"}, {0x00B,"EM Microelectronic"}, {0x00C,"Renesas"}, {0x00D,"MStar"},
            {0x00E,"Tyco"}, {0x00F,"Quanray Electronics"}, {0x010,"Fujitsu"},
            {0x011,"ILIAN Technology"}, {0x012,"Digital Angel"}, {0x013,"SETec"}, {0x014,"Qualcomm"},
        };

        // Modely dle (MDID, TMN) – nejbeznejsi cipy (heuristika)
        static readonly Dictionary<(int, int), string> Models = new Dictionary<(int, int), string>
        {
            // Impinj
            {(0x001,0x100),"Monza 4QT"}, {(0x001,0x10C),"Monza 4E"}, {(0x001,0x130),"Monza 4D"},
            {(0x001,0x140),"Monza 4i"}, {(0x001,0x150),"Monza 5"}, {(0x001,0x160),"Monza R6-A"},
            {(0x001,0x170),"Monza R6"}, {(0x001,0x171),"Monza R6-P"}, {(0x001,0x172),"Monza R6-P (16k)"},
            {(0x001,0x180),"Monza X-2K/X-8K Dura"}, {(0x001,0x190),"M730"}, {(0x001,0x191),"M750"},
            {(0x001,0x192),"M770"}, {(0x001,0x1A0),"M775"},
            // NXP
            {(0x006,0x580),"UCODE G2iL"}, {(0x006,0x584),"UCODE G2iL+"}, {(0x006,0x603),"UCODE G2iM"},
            {(0x006,0x604),"UCODE G2iM+"}, {(0x006,0x806),"UCODE 7"}, {(0x006,0x807),"UCODE 7"},
            {(0x006,0x80A),"UCODE 7xm"}, {(0x006,0x891),"UCODE 8"}, {(0x006,0x910),"UCODE 8"},
            {(0x006,0x911),"UCODE 8m"}, {(0x006,0x912),"UCODE 8"}, {(0x006,0x913),"UCODE 8m"},
            {(0x006,0x994),"UCODE 9"}, {(0x006,0x9A0),"UCODE DNA"}, {(0x006,0x9B0),"UCODE DNA City"},
            // Alien
            {(0x003,0x403),"Higgs-2"}, {(0x003,0x412),"Higgs-3"}, {(0x003,0x414),"Higgs-4"},
            {(0x003,0x415),"Higgs-4"}, {(0x003,0x424),"Higgs-9"}, {(0x003,0x441),"Higgs-EC"},
            // EM Microelectronic
            {(0x008,0x325),"EM4325"}, {(0x008,0x423),"EM4423"}, {(0x008,0x425),"EM4425 (NFC+UHF)"},
            {(0x00B,0x325),"EM4325"}, {(0x00B,0x423),"EM4423"}, {(0x00B,0x425),"EM4425"},
            // STMicro
            {(0x007,0x100),"XRAG2"},
        };

        static readonly Dictionary<int, string> Regions = new Dictionary<int, string>
        {
            {1,"Cina (840-845 MHz)"}, {2,"Cina (920-925 MHz)"}, {3,"ETSI / EU (865-868 MHz)"},
            {4,"USA / FCC (902-928 MHz)"}, {6,"Korea"}, {7,"Japonsko"},
        };

        public static string RegionName(int r) => Regions.TryGetValue(r, out var s) ? s : "?";

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
            o.Model = Models.TryGetValue((mdid, tmn), out var m) ? m : null;

            o.Serialized = tid.Length >= 8 && HasNonZero(tid, 4);
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
            {0x30,"SGTIN-96 (produkt/GTIN)"}, {0x31,"SSCC-96 (logisticka jednotka)"},
            {0x32,"SGLN-96 (lokace/GLN)"}, {0x33,"GRAI-96 (vratny obal)"},
            {0x34,"GIAI-96 (majetek)"}, {0x35,"GID-96 (obecne)"}, {0x36,"GSRN-96"},
            {0x2C,"GDTI-96"}, {0x08,"DoD-96 (armada USA)"}, {0x2D,"GDTI-113"},
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
            (40,12,18,5),(37,11,21,6),(34,10,24,7),(30,9,28,8),(27,8,31,9),(24,7,34,10),(18,6,40,11)
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

        // XTID hlavicka (TID adresa 0x40 = byte 8-9), pokud je XTID bit nastaven
        public static string ParseXtid(byte[] tid)
        {
            if (tid == null || tid.Length < 10) return null;
            int xtid = (tid[8] << 8) | tid[9];
            var sb = new StringBuilder();
            sb.Append(string.Format("XTID hlavicka: 0x{0:X4}", xtid));
            bool extHdr = (xtid & 0x8000) != 0;
            int serLen = (xtid >> 12) & 0x7;      // delka serializace
            bool func = (xtid & 0x0200) != 0;
            bool bw = (xtid & 0x0100) != 0;
            sb.Append(string.Format("\n  Rozsirena hlavicka: {0}   Serializace: {1}   Optional cmd: {2}",
                extHdr ? "ano" : "ne", serLen > 0 ? "ano (" + (serLen * 48) + " bit)" : "ne", func ? "ano" : "ne"));
            return sb.ToString();
        }
    }
}
