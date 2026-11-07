using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UhfTagAnalyzer
{
    internal static class Program
    {
        [STAThread]
        static void Main()
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

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
