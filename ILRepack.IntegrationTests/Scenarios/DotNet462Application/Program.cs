using System;
using AnotherClassLibrary;

namespace DotNet462Application
{
    public class Program
    {
        public static void Main(string[] args)
        {
            int number = new BclAsyncUsage().GetNumber().Result;
            Console.WriteLine(number);
        }
    }
}
