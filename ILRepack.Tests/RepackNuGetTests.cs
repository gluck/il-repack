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
        [TestCase("Antlr", "3.5.0.2")]
        [TestCase("Autofac", "3.5.2")]
        [TestCase("AutoMapper", "3.3.1")]
        [TestCase("Castle.Core", "3.3.3")]
        [TestCase("Dapper", "1.40.0")]
        // Missing refs [TestCase("EntityFramework", "6.1.3")]
        // FIXME [TestCase("FSharp.Core", "3.1.2.1")]
        [TestCase("Iesi.Collections", "4.0.1.4000")]
        [TestCase("MahApps.Metro", "1.1.2")]
        [TestCase("Microsoft.Bcl", "1.1.10")]
        [TestCase("Microsoft.TeamFoundation.Common", "12.0.21005.1")]
        [TestCase("Newtonsoft.Json", "6.0.8")]
        [TestCase("NHibernate", "4.0.3.4000")]
        [TestCase("Ninject", "3.2.2")]
        // PCL [TestCase("Remotion.Linq", "1.15.15")]
        [TestCase("RestSharp", "105.0.1")]
        [TestCase("Rx-Core", "2.2.5")]
        [TestCase("Rx_Experimental-Main", "1.1.11111")]
        [TestCase("SharpZipLib", "0.86.0")]
        [TestCase("System.Spatial", "5.6.4")]
        public void RoundtripNupkg(string package, string version)
        {
            var count = NuGetHelpers.GetNupkgContentAsync(package, version)
            .Where(t => Path.GetExtension(t.Item1) == ".dll")
            .Where(t => new[] { @"lib", @"lib\20", @"lib\net20", @"lib\net35", @"lib\net40", @"lib\net4", @"lib\net45" }.Contains(Path.GetDirectoryName(t.Item1).ToLower()))
            .Do(t => SaveAs(t.Item2(), "foo.dll"))
            .Do(file => {
                Console.WriteLine("Merging {0}", file.Item1);
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

        private static void SaveAs(Stream input, string fileName)
        {
            var path = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
                Directory.CreateDirectory(path);
            using (var stream = input) {
                using (var file = new FileStream(fileName, FileMode.Create)) {
                    stream.CopyTo(file);
                }
            }
        }
    }
}
