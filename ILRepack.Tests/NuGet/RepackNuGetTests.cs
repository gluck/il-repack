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
using System.Text.RegularExpressions;

namespace ILRepack.Tests.NuGet
{
    public class RepackNuGetTests
    {
        string tempDirectory;

        [SetUp]
        public void GenerateTempFolder()
        {
            tempDirectory = TestHelpers.GenerateTempFolder();
        }

        [TearDown]
        public void CleanupTempFolder()
        {
            TestHelpers.CleanupTempFolder(ref tempDirectory);
        }

        [TestCaseSource(typeof(Data), "Packages")]
        public void RoundtripNupkg(Package p)
        {
            var count = NuGetHelpers.GetNupkgAssembliesAsync(p)
            .Do(t => TestHelpers.SaveAs(t.Item2(), tempDirectory, "foo.dll"))
            .Do(file => RepackFoo(file.Item1))
            .ToEnumerable().Count();
            Assert.IsTrue(count > 0);
        }

        [Category("LongRunning")]
        [TestCaseSource(typeof(Data), "Platforms", Category = "ComplexTests")]
        public void NupkgPlatform(Platform platform)
        {
            Observable.ToObservable(platform.Packages)
            .SelectMany(NuGetHelpers.GetNupkgAssembliesAsync)
            .Do(lib => TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1))
            .Select(lib => Path.GetFileName(lib.Item1))
            .ToList()
            .Do(list =>
            {
                Assert.IsTrue(list.Count >= platform.Packages.Count());
                Console.WriteLine("Merging {0}", string.Join(",",list));
                TestHelpers.DoRepackForCmd(new []{"/out:"+Tmp("test.dll"), "/lib:"+tempDirectory}.Concat(platform.Args).Concat(list.Select(Tmp)));
                Assert.IsTrue(File.Exists(Tmp("test.dll")));
            }).First();
        }

        string Tmp(string file)
        {
            return Path.Combine(tempDirectory, file);
        }

        static IEnumerable<string> GetPortable(string lib)
        {
            if (lib.StartsWith(@"lib")) lib = lib.Substring(4);
            var dirName = Path.GetDirectoryName(lib);
            if (string.IsNullOrEmpty(dirName) || !dirName.StartsWith(@"portable-")) return Enumerable.Empty<string>();
            dirName = dirName.Substring(9);
            return new Regex(@"\+|%2[bB]").Split(dirName);
        }

        void RepackFoo(string assemblyName)
        {
            Console.WriteLine("Merging {0}", assemblyName);
            TestHelpers.DoRepackForCmd("/out:"+Tmp("test.dll"), Tmp("foo.dll"));
            Assert.IsTrue(File.Exists(Tmp("test.dll")));
        }
    }
}
