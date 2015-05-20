using System;
using System.Windows;

namespace LibraryUserControlUsageInXAML
{
    public class Program
    {
        [STAThread]
        public static int Main()
        {
            try
            {
                new Application().Run(new MainWindow());
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }
        }
    }
}
