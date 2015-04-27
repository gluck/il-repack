using ICSharpCode.SharpZipLib.Zip;
using ILRepacking;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;

namespace ILRepack.Tests
{
    class RepackNuGetTests
    {
        [TestCase("Rx_Experimental-Main", "1.1.11111")]
        [TestCase("Rx-Core", "2.2.5")]
        // FIXME [TestCase("FSharp.Core", "3.1.2.1")]
        public void RoundtripNupkg(string package, string version)
        {
            var count = NuGetHelpers.GetNupkgContentAsync(package, version)
            .Where(t => Path.GetExtension(t.Item1) == ".dll")
            .Where(t => new [] { @"lib\net35", @"lib\net40", @"lib\net4" } .Contains(Path.GetDirectoryName(t.Item1).ToLower()))
            .Do(t => {
                using (var stream = t.Item2()) {
                    using (var file = new FileStream("foo.dll", FileMode.Create)) {
                        stream.CopyTo(file);
                    }
                }
            }).Do(file => {
                Console.WriteLine("Merging {0}", file);
                ICommandLine commandLine = new CommandLine(new []{"/out:test.dll","foo.dll","/log"});
                ILogger logger = new RepackLogger();
                RepackOptions options = new RepackOptions(commandLine, logger, new FileWrapper());
                options.Parse();
                var repack = new ILRepacking.ILRepack(options, logger);
                repack.Repack();
                Assert.IsTrue(File.Exists("test.dll"));
            }).ToEnumerable().Count();
            Assert.IsTrue(count > 0);
        }
    }
}
