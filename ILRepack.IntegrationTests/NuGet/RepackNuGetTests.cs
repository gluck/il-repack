using ILRepack.IntegrationTests.Helpers;
using ILRepack.IntegrationTests.Peverify;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using SourceLink;

namespace ILRepack.IntegrationTests.NuGet
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
            .Do(_ => VerifyTest(new[] { "foo.dll" }))
            .ToEnumerable().Count();
            Assert.IsTrue(count > 0);
        }

        [Category("LongRunning")]
        [Platform(Include = "win")]
        [TestCaseSource(typeof(Data), "Platforms", Category = "ComplexTests")]
        public void NupkgPlatform(Platform platform)
        {
            Observable.ToObservable(platform.Packages)
            .SelectMany(NuGetHelpers.GetNupkgAssembliesAsync)
            .Do(lib => TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1))
            .Select(lib => Path.GetFileName(lib.Item1))
            .ToList()
            .Do(list => RepackPlatform(platform, list))
            .First();
            var errors = PeverifyHelper.Peverify(tempDirectory, "test.dll").Do(Console.WriteLine).ToErrorCodes().ToEnumerable();
            Assert.IsFalse(errors.Contains(PeverifyHelper.VER_E_STACK_OVERFLOW));
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
            Observable.ToObservable(platform.Packages)
            .SelectMany(NuGetHelpers.GetNupkgAssembliesAsync)
            .Do(lib => TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1))
            .Select(lib => Path.GetFileName(lib.Item1))
            .ToList()
            .Do(list => RepackPlatform(platform, list))
            .First();
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
                .WithExtraArgs("/keyfile:../../../ILRepack/ILRepack.snk");
            Observable.ToObservable(platform.Packages)
            .SelectMany(NuGetHelpers.GetNupkgAssembliesAsync)
            .Do(lib => TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1))
            .Select(lib => Path.GetFileName(lib.Item1))
            .ToList()
            .Do(list => RepackPlatform(platform, list))
            .First();
            var errors = PeverifyHelper.Peverify(tempDirectory, "test.dll").Do(Console.WriteLine).ToErrorCodes().ToEnumerable();
            Assert.IsFalse(errors.Contains(PeverifyHelper.META_E_CA_FRIENDS_SN_REQUIRED));
        }

        [Test]
        [Platform(Include = "win")]
        public void VerifiesPdbAreGenerated()
        {
            var platform = Platform.From(Package.From("SourceLink.Core", "1.1.0"));
            platform.Packages.ToObservable()
                .SelectMany(NuGetHelpers.GetNupkgContentAsync)
                .Do(lib => TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1))
                .Select(lib => Path.GetFileName(lib.Item1))
                .ToList()
                .Select(pathes => pathes.Where(path => path.EndsWith("dll")).ToList())
                .Do(list => RepackPlatform(platform, list))
                .First();

            Assert.IsTrue(File.Exists(Tmp("test.pdb")));
        }

        [Test]
        [Platform(Include = "win")]
        public void VerifiesMergedPdbKeepSourceIndexationForTfsIndexation()
        {
            const string LibName = "TfsEngine.dll";
            const string PdbName = "TfsEngine.pdb";

            var platform = Platform.From(Package.From("TfsIndexer", "1.2.4"));
            platform.Packages.ToObservable()
                .SelectMany(NuGetHelpers.GetNupkgContentAsync)
                .Where(lib => new[] { LibName, PdbName }.Any(lib.Item1.EndsWith))
                .Do(lib => TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1))
                .ToArray() // to download PDB file as well
                .SelectMany(_ => _)
                .Select(lib => Path.GetFileName(lib.Item1))
                .Where(path => path.EndsWith("dll"))
                .Do(path => RepackPlatform(platform, new List<string> { path }))
                .Single();

            Assert.IsTrue(File.ReadAllText(Tmp(PdbName)).Contains("SRCSRV"), "SRCSRV should be present in " + PdbName);
            CollectionAssert.AreEqual(
                PdbFile.readSrcSrvBytes(Tmp(PdbName)),
                PdbFile.readSrcSrvBytes(Tmp("test.pdb")));
        }

        [Test]
        [Platform(Include = "win")]
        public void VerifiesMergedPdbKeepSourceIndexationForHttpIndexation()
        {
            var platform = Platform.From(
                Package.From("SourceLink.Core", "1.1.0"),
                Package.From("sourcelink.symbolstore", "1.1.0"));
            platform.Packages.ToObservable()
                .SelectMany(NuGetHelpers.GetNupkgContentAsync)
                .Do(lib => TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1))
                .Select(lib => Path.GetFileName(lib.Item1))
                .Where(path => path.EndsWith("dll"))
                .ToArray()
                .Do(path => RepackPlatform(platform, path))
                .Single();

            var primarySources = GetSourceLinks(Tmp("SourceLink.Core.pdb"));
            var otherSources = GetSourceLinks(Tmp("SourceLink.SymbolStore.pdb"));
            var mergedSources = GetSourceLinks(Tmp("test.pdb"));
            CollectionAssert.AreEquivalent(primarySources.Union(otherSources), mergedSources);
        }

        private IEnumerable<string> GetSourceLinks(string pdbName)
        {
            var sourcLinkOutput = new List<string>();
            var sourceLinkProcess = new Process();
            sourceLinkProcess.FileName = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                @"..\..\..\packages\SourceLink.1.1.0\tools\SourceLink.exe");
            sourceLinkProcess.Arguments = "srctoolx --pdb " + Tmp(pdbName);
            sourceLinkProcess.Stdout.AddHandler((_, line) => sourcLinkOutput.Add(line));
            sourceLinkProcess.Run();
            return sourcLinkOutput.Take(sourcLinkOutput.Count - 1).Skip(1);
        }

        void RepackPlatform(Platform platform, IList<string> list)
        {
            Assert.IsTrue(list.Count >= platform.Packages.Count());
            Console.WriteLine("Merging {0}", string.Join(",",list));
            TestHelpers.DoRepackForCmd(new []{"/out:"+Tmp("test.dll"), "/lib:"+tempDirectory}.Concat(platform.Args).Concat(list.Select(Tmp).OrderBy(x => x)));
            Assert.IsTrue(File.Exists(Tmp("test.dll")));
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
