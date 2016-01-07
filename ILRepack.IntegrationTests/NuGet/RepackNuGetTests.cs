using ILRepack.IntegrationTests.Helpers;
using ILRepack.IntegrationTests.Peverify;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;

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
        [TestCaseSource(typeof(Data), "Platforms", Category = "ComplexTests")]
        public void NupkgPlatform(Platform platform)
        {
            var files = Observable.ToObservable(platform.Packages)
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
