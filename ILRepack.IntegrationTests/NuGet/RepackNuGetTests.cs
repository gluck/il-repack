using ILRepack.IntegrationTests.Helpers;
using ILRepack.IntegrationTests.Peverify;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.PlatformServices;
using ILRepacking.Steps.SourceServerData;

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
            var assemblyNames = new List<string>();

            foreach (var package in packages)
            {
                var assemblies = NuGetHelpers.GetNupkgAssembliesAsync(package, fileFilter);
                foreach (var assembly in assemblies)
                {
                    string fileName = Path.GetFileName(assembly.name);
                    if (fileFilter != null && !fileFilter(fileName))
                    {
                        continue;
                    }

                    TestHelpers.SaveAs(assembly.stream, tempDirectory, fileName);
                    assemblyNames.Add(fileName);
                }
            }

            return assemblyNames;
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
        public void VerifiesMergedSignedAssemblyHasNoUnsignedFriend()
        {
            var platform = Platform.From(
                Package.From("reactiveui-core", "6.5.0")
                    .WithArtifact(@"lib\net45\ReactiveUI.dll"),
                Package.From("Splat", "1.6.2")
                    .WithArtifact(@"lib\net45\Splat.dll"))
                .WithExtraArgs("/keyfile:../../../../ILRepack/ILRepack.snk");

            var assemblyNames = DownloadPackages(platform.Packages);
            RepackPlatform(platform, assemblyNames);

            var errors = PeverifyHelper.Peverify(tempDirectory, "test.dll").Do(Console.WriteLine).ToErrorCodes().ToEnumerable();
            Assert.IsFalse(errors.Contains(PeverifyHelper.META_E_CA_FRIENDS_SN_REQUIRED));
        }

        [Test]
        [Platform(Include = "win")]
        public void VerifiesMergedPdbUnchangedSourceIndexationForTfsIndexation()
        {
            var platform = Platform.From(Package.From("TfsIndexer", "1.2.4"));

            var assemblyNames = DownloadPackages(platform.Packages, s => Path.GetFileName(s).StartsWith("TfsEngine.", StringComparison.OrdinalIgnoreCase));
            RepackPlatform(platform, new[] { "TfsEngine.dll" });

            var expected = GetSrcSrv(Tmp("TfsEngine.pdb"));
            var actual = GetSrcSrv(Tmp("test.pdb"));
            CollectionAssert.AreEqual(expected, actual);
        }

        private static IEnumerable<string> GetSrcSrv(string pdb)
        {
            return new PdbStr().Read(pdb).GetLines();
        }

        [Test]
        [Platform(Include = "win")]
        public void VerifiesMergedPdbKeepSourceIndexationForHttpIndexation()
        {
            var platform = Platform.From(
                Package.From("SourceLink.Core", "1.1.0"),
                Package.From("sourcelink.symbolstore", "1.1.0"));

            var assemblyNames = DownloadPackages(platform.Packages);
            RepackPlatform(platform, assemblyNames);

            AssertSourceLinksAreEquivalent(
                new[] { "SourceLink.Core.pdb", "SourceLink.SymbolStore.pdb", "SourceLink.SymbolStore.CorSym.pdb" }.Select(Tmp),
                Tmp("test.pdb"));
        }

        private void AssertSourceLinksAreEquivalent(IEnumerable<string> expectedPdbNames, string actualPdbName)
        {
            CollectionAssert.AreEquivalent(expectedPdbNames.SelectMany(GetSourceLinks), GetSourceLinks(actualPdbName));
        }

        private static IEnumerable<string> GetSourceLinks(string pdbName)
        {
            var processInfo = new ProcessStartInfo
                              {
                                  CreateNoWindow = true,
                                  UseShellExecute = false,
                                  RedirectStandardOutput = true,
                                  FileName = Path.Combine(
                                      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                          @".nuget\packages\SourceLink\1.1.0\tools\SourceLink.exe"),
                                  Arguments = "srctoolx --pdb " + pdbName
                              };
            using (var sourceLinkProcess = Process.Start(processInfo))
            using (StreamReader reader = sourceLinkProcess.StandardOutput)
            {
                return reader.ReadToEnd()
                        .GetLines()
                        .Take(reader.ReadToEnd().GetLines().ToArray().Length - 1)
                        .Skip(1);
            }
        }

        void RepackPlatform(Platform platform, IList<string> list)
        {
            Assert.IsTrue(list.Count >= platform.Packages.Count(), 
                "There should be at least the same number of .dlls as the number of packages");
            Console.WriteLine("Merging {0}", string.Join(",",list));
            TestHelpers.DoRepackForCmd(new []{"/out:"+Tmp("test.dll"), "/lib:"+tempDirectory}.Concat(platform.Args).Concat(list.Select(Tmp).OrderBy(x => x)));
            Assert.IsTrue(File.Exists(Tmp("test.dll")));
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
