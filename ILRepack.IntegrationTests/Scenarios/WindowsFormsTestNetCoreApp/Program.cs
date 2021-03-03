using System;
using System.Windows.Forms;

namespace WindowsFormsTestNetCoreApp
{
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
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
