using System;
using System.Runtime.CompilerServices;
using System.Windows;

namespace NestedLibraryUsageInXAML
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

        internal static int Counter { get; private set; }

        [ModuleInitializer]
        internal static void TheInitializer()
        {
            Counter++;
            Counter *= AnotherClassLibrary.ModuleInitializers.MakeInitialized.Counter;
        }

        [ModuleInitializer]
        internal static void TheInitializer2()
        {
            Counter++;
        }
    }
}
