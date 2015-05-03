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

namespace ILRepack.Tests.NuGet
{
    public class RepackNuGetTests
    {
        static readonly ILogger logger = new RepackLogger();

        string tempDirectory;

        [SetUp]
        public void GenerateTempFolder()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
        }

        [TearDown]
        public void CleanupTempFolder()
        {
            if (tempDirectory == null || !Directory.Exists(tempDirectory)) return;
            Directory.Delete(tempDirectory, true);
        }

        [TestCase("Antlr", "3.5.0.2")]
        [TestCase("Autofac", "3.5.2")]
        [TestCase("AutoMapper", "3.3.1")]
        [TestCase("Castle.Core", "3.3.3")]
        [TestCase("Dapper", "1.40.0")]
        [TestCase("FSharp.Core", "3.1.2.1")]
        [TestCase("Iesi.Collections", "4.0.1.4000")]
        [TestCase("MahApps.Metro", "1.1.2")]
        [TestCase("Microsoft.Bcl", "1.1.10")]
        [TestCase("Microsoft.TeamFoundation.Common", "12.0.21005.1")]
        [TestCase("Newtonsoft.Json", "6.0.8")]
        [TestCase("NHibernate", "4.0.3.4000")]
        [TestCase("Ninject", "3.2.2")]
        [TestCase("RestSharp", "105.0.1")]
        [TestCase("Rx-Core", "2.2.5")]
        [TestCase("Rx_Experimental-Main", "1.1.11111")]
        [TestCase("SharpZipLib", "0.86.0")]
        [TestCase("System.Spatial", "5.6.4")]
        public void RoundtripNupkg(string package, string version)
        {
            var supportedFwks = new[] { @"lib", @"lib\20", @"lib\net20", @"lib\net35", @"lib\net40", @"lib\net4", @"lib\net45" };
            var p = Package.From(package, version).WithMatcher(file => supportedFwks.Contains(Path.GetDirectoryName(file).ToLower()) );
            var count = NuGetHelpers.GetNupkgAssembliesAsync(p)
            .Do(t => SaveInTmpDirAs(t.Item2(), "foo.dll"))
            .Do(file => RepackFoo(file.Item1))
            .ToEnumerable().Count();
            Assert.IsTrue(count > 0);
        }

        static Package ikvm = Package.From("IKVM", "8.0.5449.1")
            .WithMatcher(file => string.Equals("lib", Path.GetDirectoryName(file), StringComparison.InvariantCultureIgnoreCase));

        static IEnumerable<Platform> platforms = Platform.From(
            Package.From("MassTransit", "2.9.9"),
            Package.From("Magnum", "2.1.3"),
            Package.From("Newtonsoft.Json", "6.0.8")
        ).WithFwks("net35", "net40").Concat(new [] { Platform.From(ikvm)
        });

        [TestCaseSource("platforms")]
        public void NupkgPlatform(Platform platform)
        {
            Observable.ToObservable(platform.Packages)
            .SelectMany(NuGetHelpers.GetNupkgAssembliesAsync)
            .Do(lib => SaveInTmpDirAs(lib.Item2(), lib.Item1))
            .Select(lib => Path.GetFileName(lib.Item1))
            .ToList()
            .Do(list =>
            {
                Assert.IsTrue(list.Count >= platform.Packages.Count());
                Console.WriteLine("Merging {0}", string.Join(",",list));
                DoRepackForCmd(new []{"/out:"+Tmp("test.dll"), "/lib:"+tempDirectory}.Concat(list.Select(Tmp)));
                Assert.IsTrue(File.Exists(Tmp("test.dll")));
            }).First();
        }

        void SaveInTmpDirAs(Stream input, string fileName)
        {
            var path = Path.Combine(tempDirectory, Path.GetFileName(fileName));
            using (var stream = input) {
                using (var file = new FileStream(path, FileMode.Create)) {
                    stream.CopyTo(file);
                }
            }
        }

        static void DoRepackForCmd(params string[] args)
        {
            DoRepackForCmd((IEnumerable<string>)args);
        }

        static void DoRepackForCmd(IEnumerable<string> args)
        {
            var repack = new ILRepacking.ILRepack(GetOptionsForCmd(args), logger);
            repack.Repack();
        }

        static RepackOptions GetOptionsForCmd(IEnumerable<string> args)
        {
            ICommandLine commandLine = new CommandLine(args.Concat(new []{"/log"}).ToArray());
            RepackOptions options = new RepackOptions(commandLine, logger, new FileWrapper());
            options.Parse();
            return options;
        }

        string Tmp(string file)
        {
            return Path.Combine(tempDirectory, file);
        }

        void RepackFoo(string assemblyName)
        {
            Console.WriteLine("Merging {0}", assemblyName);
            DoRepackForCmd("/out:"+Tmp("test.dll"), Tmp("foo.dll"));
            Assert.IsTrue(File.Exists(Tmp("test.dll")));

            // let's get dirty and repack them once again with /intern
            DoRepackForCmd("/out:"+Tmp("test2.dll"), Tmp("test.dll"), Tmp("foo.dll"), "/internalize");
            Assert.IsTrue(File.Exists(Tmp("test2.dll")));
        }
    }
}
