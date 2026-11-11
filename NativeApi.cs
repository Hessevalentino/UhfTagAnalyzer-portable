using System;
using System.Runtime.InteropServices;

namespace UhfTagAnalyzer
{
    // Pametove banky EPC Gen2 / ISO 18000-6C
    public enum Bank : byte { Reserved = 0, Epc = 1, Tid = 2, User = 3 }

    // Struktury presne dle CFApi.h (byte-packed). Konvence volani = Winapi/StdCall,
    // stejne jako oficialni SDK sample Readercommon.cs pro UHFPrimeReader.dll.

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TagInfo
    {
        public ushort NO;
        public short rssi;
        public byte antenna;
        public byte channel;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public byte[] crc;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public byte[] pc;
        public byte codeLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)] public byte[] code;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TagResp
    {
        public byte tagStatus;
        public byte antenna;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public byte[] crc;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public byte[] pc;
        public byte codeLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)] public byte[] code;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DevicePara
    {
        public byte DEVICEARRD, RFIDPRO, WORKMODE, INTERFACE, BAUDRATE, WGSET, ANT, REGION;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public byte[] STRATFREI;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public byte[] STRATFRED;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public byte[] STEPFRE;
        public byte CN, RFIDPOWER, INVENTORYAREA, QVALUE, SESSION, ACSADDR, ACSDATALEN;
        public byte FILTERTIME, TRIGGLETIME, BUZZERTIME, INTENERLTIME;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DeviceFullInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] DevicehardVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] DevicefirmVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)] public byte[] DeviceSN;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] hardVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] firmVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)] public byte[] SN;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FreqInfo
    {
        public byte region;
        public ushort StartFreq;
        public ushort StopFreq;
        public ushort StepFreq;
        public byte cnt;
    }

    // LockTag: oblast (erea)
    public enum LockArea : byte { KillPwd = 0x00, AccessPwd = 0x01, Epc = 0x02, Tid = 0x03, User = 0x04 }

    // LockTag: akce (action) – dle DLL manualu
    //  pro KillPwd/AccessPwd: 0=r/w, 1=vzdy r/w, 2=s heslem r/w, 3=nelze cist/psat
    //  pro EPC/TID/User:      0=zapisovatelne, 1=vzdy zapisovatelne, 2=s heslem, 3=permalock
    public enum LockAction : byte { Open = 0x00, AlwaysOpen = 0x01, PwdProtected = 0x02, Permalock = 0x03 }

    internal static class NativeApi
    {
        const string DLL = "UHFPrimeReader.dll";

        [DllImport(DLL)] public static extern int CFHid_GetUsbCount();

        [DllImport(DLL)] public static extern bool CFHid_GetUsbInfo(ushort index, byte[] pucDeviceInfo);

        [DllImport(DLL)] public static extern int OpenHidConnection(out IntPtr handler, ushort index);

        [DllImport(DLL)] public static extern int CloseDevice(IntPtr hComm);

        [DllImport(DLL)] public static extern int GetDeviceInfo(IntPtr hComm, out DeviceFullInfo info);

        [DllImport(DLL)] public static extern int GetDevicePara(IntPtr hComm, out DevicePara info);

        [DllImport(DLL)] public static extern int GetRFPower(IntPtr hComm, out byte power, out byte reserved);

        [DllImport(DLL)] public static extern int InventoryContinue(IntPtr hComm, byte invCount, uint invParam);

        [DllImport(DLL)] public static extern int GetTagUii(IntPtr hComm, out TagInfo tag, ushort timeout);

        [DllImport(DLL)] public static extern int InventoryStop(IntPtr hComm, ushort timeout);

        [DllImport(DLL)]
        public static extern int ReadTag(IntPtr hComm, byte option, byte[] accPwd,
                                         byte memBank, ushort wordPtr, byte wordCount);

        [DllImport(DLL)]
        public static extern int GetReadTagResp(IntPtr hComm, out TagResp resp,
                                                out byte wordCount, byte[] readData, ushort timeout);

        [DllImport(DLL)]
        public static extern int WriteTag(IntPtr hComm, byte option, byte[] accPwd,
                                          byte memBank, ushort wordPtr, byte wordCount, byte[] writeData);

        // Odpoved tagu na zapis/lock/kill. cmd: 0x0004 = WRITE, 0x0005 = LOCK, 0x0006 = KILL (CFApi.h ISO_*).
        // Volat ve smycce az do -249 (konec operace) – stejne jako GetReadTagResp.
        [DllImport(DLL)]
        public static extern int GetTagResp(IntPtr hComm, ushort cmd, out TagResp resp, ushort timeout);

        [DllImport(DLL)]
        public static extern int LockTag(IntPtr hComm, byte[] accPwd, byte erea, byte action);

        [DllImport(DLL)]
        public static extern int KillTag(IntPtr hComm, byte[] killPwd);

        [DllImport(DLL)]
        public static extern int SetSelectMask(IntPtr hComm, ushort maskPtr, byte maskBits, byte[] mask);

        // konfigurace / testy
        [DllImport(DLL)] public static extern int SetRFPower(IntPtr hComm, byte power, byte reserved);
        [DllImport(DLL)] public static extern int SetDevicePara(IntPtr hComm, DevicePara info);
        [DllImport(DLL)] public static extern int GetFreq(IntPtr hComm, out FreqInfo freq);
        [DllImport(DLL)] public static extern int SetFreq(IntPtr hComm, ref FreqInfo freq);
        [DllImport(DLL)] public static extern int GetAntenna(IntPtr hComm, out byte antenna);
        [DllImport(DLL)] public static extern int SetAntenna(IntPtr hComm, ref byte antenna);
        [DllImport(DLL)] public static extern int GetRFIDType(IntPtr hComm, out byte type);
        [DllImport(DLL)] public static extern int SetRFIDType(IntPtr hComm, byte type);
        [DllImport(DLL)] public static extern int GetTemperature(IntPtr hComm, out byte cur, out byte limit);
        [DllImport(DLL)] public static extern int GetCoilPRM(IntPtr hComm, out byte qval, out byte reserved);
        [DllImport(DLL)] public static extern int SetCoilPRM(IntPtr hComm, byte qval, byte reserved);
        [DllImport(DLL)] public static extern int RebootDevice(IntPtr hComm);
        [DllImport(DLL)] public static extern int Close_Relay(IntPtr hComm, byte time);
        [DllImport(DLL)] public static extern int Release_Relay(IntPtr hComm, byte time);
    }

    // Prevod navratovych kodu knihovny (signed int32) na text.
    public static class ErrorCodes
    {
        static int S(uint u) => unchecked((int)u);

        static readonly System.Collections.Generic.Dictionary<int, string> Map =
            new System.Collections.Generic.Dictionary<int, string>
        {
            { 0, "OK" },
            { -255, "Chybny handle nebo parametr" },
            { -254, "Nepodarilo se otevrit ctecku" },
            { -253, "Interni chyba knihovny" },
            { -252, "Chybny parametr prikazu" },
            { -251, "Seriove cislo uz existuje" },
            { -250, "Interni chyba prikazu ctecky" },
            { -249, "Tag nenalezen / operace skoncila" },
            { -248, "Tag neodpovedel (timeout odpovedi tagu)" },
            { -247, "Chyba demodulace dat tagu" },
            { -246, "Data tagu presahla max. delku prenosu" },
            { -245, "Autentizace selhala" },
            { -244, "Chybne heslo" },
            { -243, "SAM neodpovida" },
            { -242, "Prikaz SAM selhal" },
            { -241, "Chybny format odpovedi ctecky" },
            { -240, "Ctecka ma dalsi data" },
            { -239, "Prichozi cache je prilis mala, data pretekla" },
            { -238, "Timeout cekani na odpoved ctecky" },
            { -237, "Chyba zapisu do ctecky" },
            { -236, "Chyba cteni z ctecky" },
            { -235, "Zadna dalsi data" },
            { -234, "Sitove spojeni nenavazano" },
            { -233, "Sitove spojeni preruseno" },
            { -232, "Chyba CRC odpovedi ctecky" },
            // stavove kody tagu
            { -192, "Tag ma malo energie (slaby signal / daleko)" },
            { -191, "Nedostatecna opravneni k operaci (tag)" },
            { -190, "Preteceni / cilova oblast neexistuje" },
            { -189, "Pametova oblast je zamcena (lock)" },
            { -188, "Chybne heslo operace tagu" },
            { -187, "Autentizace tagu selhala" },
            { -186, "Neznama chyba operace tagu" },
            { -176, "Jina chyba (ISO)" },
            { -175, "Tag nepodporuje tento parametr" },
            { -174, "Nedostatecna opravneni (ISO)" },
            { -173, "Preteceni pametove oblasti (ISO)" },
            { -172, "Pametova oblast zamcena (ISO)" },
            { -167, "Nedostatecne napajeni tagu (ISO)" },
            { -166, "Neznama chyba odpovedi tagu (ISO)" },
            { -164, "Tag je zaneprazdnen (busy)" },
        };

        public static string Text(int code)
        {
            return Map.TryGetValue(code, out var s)
                ? s
                : string.Format("Neznama chyba (kod {0} / 0x{1:X8})", code, unchecked((uint)code));
        }
    }
}
