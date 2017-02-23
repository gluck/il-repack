using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AnotherClassLibrary
{
    public class BclAsyncUsage
    {
        public async Task<int> GetNumber([CallerMemberName] string caller = "<none>")
        {
            Console.WriteLine("CallerMemberName: " + caller);

            using (var stringWriter = new StringWriter())
            {
                // use the Async extension methods
                await stringWriter.WriteAsync("42");

                // TaskEx is in the .NET 4.0 assembly
                await TaskEx.Delay(TimeSpan.FromMilliseconds(500));

                return int.Parse(stringWriter.ToString());
            }
        }
    }
}
