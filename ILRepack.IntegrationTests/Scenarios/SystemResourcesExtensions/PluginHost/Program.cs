using System;
using System.Reflection;

namespace SystemResourcesExtensions.PluginHost
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 1)
                return 1;

            var plugin = Assembly.LoadFrom(args[0]);
            var program = plugin.GetType("SystemResourcesExtensions.Program", throwOnError: true);
            var main = program.GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
            return (int)main.Invoke(null, null);
        }
    }
}
