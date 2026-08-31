using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Resources;

namespace SystemResourcesExtensions
{
    internal static class Program
    {
        private static int Main()
        {
            var resources = new ResourceManager("SystemResourcesExtensions.Resources", Assembly.GetExecutingAssembly());

            if (!string.Equals(resources.GetString("Greeting"), "hello from preserialized resources", StringComparison.Ordinal))
                return Fail("String resource was not read correctly.");

            var payload = resources.GetObject("Payload") as byte[];
            if (payload == null || !payload.SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }))
                return Fail("Byte-array resource was not read correctly.");

            var launchDate = resources.GetObject("LaunchDate");
            if (!(launchDate is DateTime) || ((DateTime)launchDate).ToUniversalTime() != new DateTime(2024, 11, 5, 6, 7, 8, DateTimeKind.Utc))
                return Fail("DateTime resource was not read correctly.");

#if SRE10
            var release = resources.GetObject("Release") as Version;
            if (release == null || release != new Version(1, 2, 3, 4))
                return Fail("Version resource was not read correctly.");
#endif

            var size = resources.GetObject("Size");
            if (!(size is Size) || (Size)size != new Size(17, 23))
                return Fail("Size resource was not read correctly.");

            using (var image = resources.GetObject("TestImage") as Bitmap)
            {
                if (image == null || image.Width != 30 || image.Height != 30)
                    return Fail("Bitmap resource was not read correctly.");
            }

            Console.WriteLine("Preserialized resources loaded successfully.");
            return 0;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine(message);
            return 1;
        }
    }
}
