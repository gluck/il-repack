using System;
using System.Buffers;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DotNet462NetStandard2
{
    class Program
    {
        static unsafe int Main(string[] args)
        {
            int a = 100500;
            var b = Marshal.AllocHGlobal(new IntPtr(4));
            Unsafe.Write(b.ToPointer(), a);
            var c = Unsafe.Read<int>(b.ToPointer());

            if (a != c)
            {
                Console.WriteLine("Unsafe failed");
                return -1;
            }
            else
            {
                Console.WriteLine("Unsafe success");
            }

            var arr = ArrayPool<int>.Shared.Rent(10);

            ArrayPool<int>.Shared.Return(arr);

            Marshal.FreeHGlobal(b);

            var types = Assembly.GetEntryAssembly().GetTypes();

            if (types.Any(t => t.Name == "Program"))
            {
                Console.WriteLine("Reflection success");
            }
            else
            {
                return -1;
            }

            return 0;
        }
    }
}
