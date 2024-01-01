using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.PlatformServices;
using System.Reflection;
using ILRepack.IntegrationTests.Helpers;
using ILRepack.IntegrationTests.Peverify;
using ILRepacking;
using ILRepacking.Steps.SourceServerData;
using NUnit.Framework;

namespace ILRepack.IntegrationTests.NuGet
{
    public class RepackNuGetTests
    {
        string tempDirectory;

        [SetUp]
        public void GenerateTempFolder()
        {
            PlatformEnlightenmentProvider.Current = new TestsPlatformEnglightenmentProvider();
            tempDirectory = TestHelpers.GenerateTempFolder();
        }

        [TearDown]
        public void CleanupTempFolder()
        {
            TestHelpers.CleanupTempFolder(ref tempDirectory);
        }

        [TestCaseSource(typeof(Data), nameof(Data.Packages))]
        public void RoundtripNupkg(Package p)
        {
            var list = NuGetHelpers.GetNupkgAssembliesAsync(p);
            Assert.IsTrue(list.Count() > 0);

            foreach (var item in list)
            {
                TestHelpers.SaveAs(item.stream, tempDirectory, "foo.dll");
                RepackFoo(item.name);
                VerifyTest(new[] { "foo.dll" });
            }
        }

        [Category("LongRunning")]
        [Platform(Include = "win")]
        [TestCaseSource(typeof(Data), nameof(Data.Platforms), Category = "ComplexTests")]
        public void NupkgPlatform(Platform platform)
        {
            var assemblyNames = DownloadPackages(platform.Packages);
            RepackPlatform(platform, assemblyNames);

            var errors = PeverifyHelper
                .Peverify(tempDirectory, "test.dll")
                .Do(Console.WriteLine)
                .ToErrorCodes().ToEnumerable();

            Assert.IsFalse(errors.Contains(PeverifyHelper.VER_E_STACK_OVERFLOW));
        }

        private IList<string> DownloadPackages(IEnumerable<Package> packages, Predicate<string> fileFilter = null)
        {
            var assemblyNames = new HashSet<string>();

            foreach (var package in packages)
            {
                var assemblies = NuGetHelpers.GetNupkgAssembliesAsync(package, fileFilter);
                foreach (var assembly in assemblies.OrderBy(s => s))
                {
                    string fileName = Path.GetFileName(assembly.name);
                    if (fileFilter != null && !fileFilter(fileName))
                    {
                        continue;
                    }

                    if (!assemblyNames.Add(fileName))
                    {
                        continue;
                    }

                    TestHelpers.SaveAs(assembly.stream, tempDirectory, fileName);
                }
            }

            return assemblyNames.ToArray();
        }

        [Test]
        [Platform(Include = "win")]
        public void VerifiesMergesBclFine()
        {
            var platform = Platform.From(
                Package.From("Microsoft.Bcl", "1.1.10")
                    .WithArtifact(@"lib\net40\System.Runtime.dll"),
                Package.From("Microsoft.Bcl", "1.1.10")
                    .WithArtifact(@"lib\net40\System.Threading.Tasks.dll"),
                Package.From("Microsoft.Bcl.Async", "1.0.168")
                    .WithArtifact(@"lib\net40\Microsoft.Threading.Tasks.dll"))
                .WithExtraArgs(@"/targetplatform:v4,C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0");

            var assemblyNames = DownloadPackages(platform.Packages);
            RepackPlatform(platform, assemblyNames);

            var errors = PeverifyHelper.Peverify(tempDirectory, "test.dll").Do(Console.WriteLine).ToErrorCodes().ToEnumerable();
            Assert.IsFalse(errors.Contains(PeverifyHelper.VER_E_TOKEN_RESOLVE));
            Assert.IsFalse(errors.Contains(PeverifyHelper.VER_E_TYPELOAD));
        }

        [Test]
        [Platform(Include = "win")]
        public void VerifiesMergesFineWhenOutPathIsOneOfInputs()
        {
            var platform = Platform.From(
                Package.From("Microsoft.Bcl", "1.1.10")
                    .WithArtifact(@"lib\net40\System.Runtime.dll"),
                Package.From("Microsoft.Bcl", "1.1.10")
                    .WithArtifact(@"lib\net40\System.Threading.Tasks.dll"),
                Package.From("Microsoft.Bcl.Async", "1.0.168")
                    .WithArtifact(@"lib\net40\Microsoft.Threading.Tasks.dll"))
                .WithExtraArgs(@"/targetplatform:v4,C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0");

            var assemblyNames = DownloadPackages(platform.Packages);
            RepackPlatformIntoPrimary(platform, assemblyNames);
        }

        [Test]
        [Platform(Include = "win")]
        public void VerifiesMergedSignedAssemblyHasNoUnsignedFriend()
        {
            var snkPath = Path.GetFullPath(
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "ILRepack.snk"));
            var platform = Platform.From(
                Package.From("reactiveui-core", "6.5.0")
                    .WithArtifact(@"lib\net45\ReactiveUI.dll"),
                Package.From("Splat", "1.6.2")
                    .WithArtifact(@"lib\net45\Splat.dll"))
                .WithExtraArgs($"/keyfile:{snkPath}");

            var assemblyNames = DownloadPackages(platform.Packages);
            RepackPlatform(platform, assemblyNames);

            var errors = PeverifyHelper.Peverify(tempDirectory, "test.dll").Do(Console.WriteLine).ToErrorCodes().ToEnumerable();
            Assert.IsFalse(errors.Contains(PeverifyHelper.META_E_CA_FRIENDS_SN_REQUIRED));
        }

        [Test]
        [Platform(Include = "win")]
        public void VerifiesMergedPdbUnchangedSourceIndexationForTfsIndexation()
        {
            // This test requires Mono.Cecil.Pdb.dll. Indicate a dependency such that
            // the reference is not accidentally removed.
            _ = typeof(Mono.Cecil.Pdb.PdbReaderProvider);

            var platform = Platform.From(
                Package.From("TfsIndexer", "1.2.4"),
                Package.From("FSharp.Core", "3.0.2"));

            var assemblyNames = DownloadPackages(
                platform.Packages,
                s =>
                {
                    string name = Path.GetFileName(s);
                    return 
                        name.StartsWith("TfsEngine.", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("FSharp.Core.dll", StringComparison.OrdinalIgnoreCase);
                });
            RepackPlatform(platform, new[] { "TfsEngine.dll", "FSharp.Core.dll" });

            var expected = GetSrcSrv(Tmp("TfsEngine.pdb"));
            var actual = GetSrcSrv(Tmp("test.pdb"));
            CollectionAssert.AreEqual(expected, actual);
        }

        private static IEnumerable<string> GetSrcSrv(string pdb)
        {
            return new PdbStr().Read(pdb).GetLines();
        }

        //[Test]
        //[Platform(Include = "win")]
        public void VerifiesMergedPdbKeepSourceIndexationForHttpIndexation()
        {
            var platform = Platform.From(
                Package.From("SourceLink.Core", "1.1.0"),
                Package.From("sourcelink.symbolstore", "1.1.0"),
                Package.From("FSharp.Core", "4.0.0.1"));

            var assemblyNames = DownloadPackages(platform.Packages);
            RepackPlatform(platform, assemblyNames);

            AssertSourceLinksAreEquivalent(
                new[] { "SourceLink.Core.pdb", "SourceLink.SymbolStore.pdb", "SourceLink.SymbolStore.CorSym.pdb" }.Select(Tmp),
                Tmp("test.pdb"));
        }

        private void AssertSourceLinksAreEquivalent(IEnumerable<string> expectedPdbNames, string actualPdbName)
        {
            var expected = expectedPdbNames.SelectMany(GetSourceLinks).ToArray();
            var actual = GetSourceLinks(actualPdbName);
            CollectionAssert.AreEquivalent(expected, actual);
        }

        private static IEnumerable<string> GetSourceLinks(string pdbName)
        {
            string exe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    @".nuget\packages\SourceLink\1.1.0\tools\SourceLink.exe");
            string arguments = "srctoolx --pdb " + pdbName;
            var process = ProcessRunner.Run(exe, arguments);
            var output = process.Output;
            var lines = output.GetLines().ToArray();
            lines = lines.Take(lines.Length - 1).Skip(1).ToArray();
            return lines;
        }

        void RepackPlatform(Platform platform, IList<string> list)
        {
            Assert.IsTrue(list.Count >= platform.Packages.Count(), 
                "There should be at least the same number of .dlls as the number of packages");
            Console.WriteLine("Merging {0}", string.Join(",",list));
            TestHelpers.DoRepackForCmd(new []{"/out:"+Tmp("test.dll"), "/lib:"+tempDirectory}.Concat(platform.Args).Concat(list.Select(Tmp).OrderBy(x => x)));
            Assert.IsTrue(File.Exists(Tmp("test.dll")));
        }

        void RepackPlatformIntoPrimary(Platform platform, IList<string> list)
        {
            list = list.OrderBy(f => f).ToList();
            var first = list.First();
            Console.WriteLine("Merging {0} into {1}", string.Join(",", list), first);
            TestHelpers.DoRepackForCmd(new[] { "/out:" + Tmp(first), "/lib:" + tempDirectory }.Concat(platform.Args).Concat(list.Select(Tmp).OrderBy(x => x)));
        }

        string Tmp(string file)
        {
            return Path.Combine(tempDirectory, file);
        }

        void VerifyTest(IEnumerable<string> mergedLibraries)
        {
            if (XPlat.IsMono) return;
            var errors = PeverifyHelper.Peverify(tempDirectory, "test.dll").Do(Console.WriteLine).ToEnumerable();
            if (errors.Any())
            {
                var origErrors = mergedLibraries.SelectMany(it => PeverifyHelper.Peverify(tempDirectory, it).ToEnumerable());
                if (errors.Count() != origErrors.Count())
                    Assert.Fail($"{errors.Count()} errors in peverify, check logs for details");
            }
        }

        void RepackFoo(string assemblyName)
        {
            Console.WriteLine("Merging {0}", assemblyName);
            TestHelpers.DoRepackForCmd("/out:"+Tmp("test.dll"), Tmp("foo.dll"));
            Assert.IsTrue(File.Exists(Tmp("test.dll")));
        }
    }
}
