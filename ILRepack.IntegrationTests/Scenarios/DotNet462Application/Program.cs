using System;
using System.Collections.Immutable;
using System.Linq;
using AnotherClassLibrary;

namespace DotNet462Application
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine(ImmutableHashSet.Create(1).First());

            int number = new BclAsyncUsage().GetNumber().Result;
            Console.WriteLine(number);
        }
    }
}
