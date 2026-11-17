using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace PlcDbSnapshot.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Values are written in invariant format so files compare the same on any PC locale.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
