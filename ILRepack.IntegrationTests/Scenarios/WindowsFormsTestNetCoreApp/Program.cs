using System;
using System.Windows.Forms;

namespace WindowsFormsTestNetCoreApp
{
    static class Program
    {
        [STAThread]
        static int Main()
        {
            try
            {
                ///Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1());
                return 0;
            }
            catch (Exception)
            {
                return 1;
            }
        }
    }
}
