using System.Collections;
using System.Collections.Generic;
using System.Linq;

using ILRepacking.Steps.SourceServerData;

using NUnit.Framework;

namespace ILRepack.Tests.Steps.SourceServerData
{
    internal static class SourceServerDataRepackStepTest
    {
        public static IEnumerable MergingTestCases
        {
            get
            {
                yield return new TestCaseData(
                        2,
                        "http",
                        "%var2%",
                        new Dictionary<string, string>
                        {
                            {
                                @"c:\proj\src\file.cs",
                                "http://server/raw/repo/commit-hash/src/files.cs"
                            },
                            {
                                @"c:\proj\test\file.cs",
                                "http://server/raw/repo/commit-hash/test/files.cs"
                            }
                        },
                        new HttpSourceServerDescriptor(
                            2,
                            "http",
                            "http://server/raw/repo/commit-hash/%var2%",
                            new[]
                            {
                                new SourceFileDescriptor(@"c:\proj\src\file.cs", "src/files.cs"),
                                new SourceFileDescriptor(@"c:\proj\test\file.cs", "test/files.cs")
                            }),
                        new HttpSourceServerDescriptor[0]).SetName("One assembly");
                yield return new TestCaseData(
                        2,
                        "http",
                        "%var2%",
                        new Dictionary<string, string>
                        {
                            {
                                @"c:\primary\src\file1.cs",
                                "http://server/raw/primary/primary-commit-hash/src/files1.cs"
                            },
                            {
                                @"c:\proj1\sources\file2.cs",
                                "http://server/raw/proj1/commit-hash/sources/files2.cs"
                            },
                            {
                                @"c:\proj2\main\file3.cs",
                                "http://server/raw/proj2/commit-hash/main/files3.cs"
                            }
                        },
                        new HttpSourceServerDescriptor(
                            2,
                            "http",
                            "http://server/raw/primary/primary-commit-hash/%var2%",
                            new[] { new SourceFileDescriptor(@"c:\primary\src\file1.cs", "src/files1.cs") }),
                        new[]
                        {
                            new HttpSourceServerDescriptor(
                                2,
                                "http",
                                "http://server/raw/proj1/commit-hash/%var2%",
                                new[] { new SourceFileDescriptor(@"c:\proj1\sources\file2.cs", "sources/files2.cs") }),
                            new HttpSourceServerDescriptor(
                                2,
                                "http",
                                "http://server/raw/proj2/commit-hash/%var2%",
                                new[] { new SourceFileDescriptor(@"c:\proj2\main\file3.cs", "main/files3.cs") })
                        })
                        .SetName("Tree assemblies with the same root web server, same version control and same version.");
                yield return new TestCaseData(
                        2,
                        "http",
                        "%var2%",
                        new Dictionary<string, string>
                        {
                            {
                                @"c:\primary\src\file1.cs",
                                "http://server/raw/primary/primary-commit-hash/src/files1.cs"
                            },
                            {
                                @"c:\other-proj\src\file2.cs",
                                "http://server/raw/other-proj/commit-hash/src/files2.cs"
                            }
                        },
                        new HttpSourceServerDescriptor(
                            2,
                            "http",
                            "http://server/raw/primary/primary-commit-hash/%var2%",
                            new[] { new SourceFileDescriptor(@"c:\primary\src\file1.cs", "src/files1.cs") }),
                        new[]
                        {
                            new HttpSourceServerDescriptor(
                                55,
                                "http",
                                "http://server/raw/other-proj/commit-hash/%var2%",
                                new[] { new SourceFileDescriptor(@"c:\other-proj\src\file2.cs", "src/files2.cs") })
                        })
                        .SetName("Two assemblies with the same root web server, same version control but different version.");
                yield return new TestCaseData(
                        2,
                        "http",
                        "%var2%",
                        new Dictionary<string, string>
                        {
                            {
                                @"c:\primary\src\file1.cs",
                                "http://server/raw/primary/primary-commit-hash/src/files1.cs"
                            }
                        },
                        new HttpSourceServerDescriptor(
                            2,
                            "http",
                            "http://server/raw/primary/primary-commit-hash/%var2%",
                            new[] { new SourceFileDescriptor(@"c:\primary\src\file1.cs", "src/files1.cs") }),
                        new[]
                        {
                            new HttpSourceServerDescriptor(
                                2,
                                "tfs",
                                "tfs://server/raw/other-proj/commit-hash/%var2%",
                                new[] { new SourceFileDescriptor(@"c:\other-proj\src\file2.cs", "src/files2.cs") })
                        })
                        .SetName("Two assemblies with different version control");
            }
        }

        [TestCaseSource(nameof(MergingTestCases))]
        public static void GivenAListOfSourceServerDescriptor_WhenMergingThem_ThenItShouldBeProperlyMerged(
            int expectedVersion,
            string expectedVersionControl,
            string expectedTarget,
            IDictionary<string, string> expectedSourceFiles,
            HttpSourceServerDescriptor primary,
            IEnumerable<HttpSourceServerDescriptor> other)
        {
            var merged = SourceServerDataRepackStep.MergeHttpSourceServerData(primary, other);
            Assert.That(merged.Version, Is.EqualTo(expectedVersion));
            Assert.That(merged.VersionControl, Is.EqualTo(expectedVersionControl));
            Assert.That(merged.Target, Is.EqualTo(expectedTarget));
            CollectionAssert.AreEquivalent(
                expectedSourceFiles,
                merged.SourceFiles.ToDictionary(file => file.Path, file => file.Variable2));
        }
    }
}
