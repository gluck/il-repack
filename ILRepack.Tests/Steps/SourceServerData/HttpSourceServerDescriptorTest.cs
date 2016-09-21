using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ILRepacking.Steps.SourceServerData;
using NUnit.Framework;

namespace ILRepack.Tests.Steps.SourceServerData
{
    internal static class HttpSourceServerDescriptorTest
    {
        public static IEnumerable SuccessfulParsingTestCases
        {
            get
            {
                yield return new TestCaseData(
                    @"SRCSRV: ini ------------------------------------------------
VERSION=2
SRCSRV: variables ------------------------------------------
SRCSRVVERCTRL=http
SRCSRVTRG=http://website/raw/myrepo/commithash/%var2%
SRCSRV: source files ---------------------------------------
C:\Users\vagrant\workspace\myrepo\src\File1.cs*src/File1.cs
C:\Users\vagrant\workspace\myrepo\src\FileTwo.cs*src/FileTow.cs
C:\Users\vagrant\workspace\myrepo\src\FileXxx.cs*src/FileXxx.cs
SRCSRV: end ------------------------------------------------",
                    2,
                    "http",
                    "http://website/raw/myrepo/commithash/%var2%",
                    3).SetName("3 files, http");
                yield return new TestCaseData(
                    @"SRCSRV: ini ------------------------------------------------
VERSION=1
SRCSRV: variables ------------------------------------------
SRCSRVTRG=https://website/raw/anotherrepo/commithash/%var2%
SRCSRVVERCTRL=https
SRCSRV: source files ---------------------------------------
SRCSRV: end ------------------------------------------------",
                    1,
                    "https",
                    "https://website/raw/anotherrepo/commithash/%var2%",
                    0).SetName("No files, https");
                yield return new TestCaseData(
                    @"SRCSRV: ini ------------------------------------------------

VERSION=2

SRCSRV: variables ------------------------------------------

SRCSRVVERCTRL=http

SRCSRVTRG=http://website/raw/myrepo/commithash/%var2%

SRCSRV: source files ---------------------------------------

C:\Users\vagrant\workspace\myrepo\src\File1.cs*src/File1.cs

C:\Users\vagrant\workspace\myrepo\src\FileTwo.cs*src/FileTow.cs

SRCSRV: end ------------------------------------------------",
                    2,
                    "http",
                    "http://website/raw/myrepo/commithash/%var2%",
                    2).SetName("2 files, http, different return line");
                yield return new TestCaseData(
                    @"SRCSRV: ini ------------------------------------------------
VERSION=1
SRCSRV: variables ------------------------------------------
SRCSRVTRG=https://website/raw/anotherrepo/commit=hash/%var2%
SRCSRVVERCTRL=http
SRCSRV: source files ---------------------------------------
C:\Users\vagrant\workspace\myrepo\src\OnlyOneFile.cs*src/OnlyOneFile.cs
SRCSRV: end ------------------------------------------------",
                    1,
                    "http",
                    "https://website/raw/anotherrepo/commit=hash/%var2%",
                    1).SetName("1 file, http, equal charater in SRCSRVTRG");
            }
        }

        [TestCaseSource(nameof(SuccessfulParsingTestCases))]
        public static void GivenHttpRawSourceServerData_WhenParsing_ThenValuesShouldTHeExpectedOne(
            string raw,
            int expectedVersion,
            string expectedVersionControl,
            string expectedTarget,
            int expectedSourceFilesCount)
        {
            HttpSourceServerDescriptor descriptor;
            var success = HttpSourceServerDescriptor.TryParse(raw, out descriptor);
            Assert.IsTrue(success);
            Assert.That(descriptor.Version, Is.EqualTo(expectedVersion));
            Assert.That(descriptor.VersionControl, Is.EqualTo(expectedVersionControl));
            Assert.That(descriptor.Target, Is.EqualTo(expectedTarget));
            Assert.That(descriptor.SourceFiles.Length, Is.EqualTo(expectedSourceFilesCount));
        }
        public static IEnumerable FailingParsingTestCases
        {
            get
            {
                yield return @"SRCSRV: ini ------------------------------------------------
VERSION=2
SRCSRV: variables ------------------------------------------
SRCSRVVERCTRL=tfs
SRCSRV: source files ---------------------------------------
SRCSRV: end ------------------------------------------------";
                yield return "";
            }
        }

        [TestCaseSource(nameof(FailingParsingTestCases))]
        public static void GivenInvliadRawSourceServerData_WhenParsing_ThenValuesShouldTHeExpectedOne(string raw)
        {
            HttpSourceServerDescriptor descriptor;
            var success = HttpSourceServerDescriptor.TryParse(raw, out descriptor);
            Assert.IsFalse(success);
        }

        public static IEnumerable ToStringTestCases
        {
            get
            {
                yield return new TestCaseData(new HttpSourceServerDescriptor(
                            3,
                            "http",
                            "http://website/raw/therepo/commit-hash/%var2%",
                            new[]
                            {
                                new SourceFileDescriptor(@"c:\project\folder\file1.cs", "folder/file1.cs"),
                                new SourceFileDescriptor(@"c:\project\folder\file2.cs", "folder/file2.cs")
                            }))
                            .Returns(@"SRCSRV: ini ------------------------------------------------
VERSION=3
SRCSRV: variables ------------------------------------------
SRCSRVVERCTRL=http
SRCSRVTRG=http://website/raw/therepo/commit-hash/%var2%
SRCSRV: source files ---------------------------------------
c:\project\folder\file1.cs*folder/file1.cs
c:\project\folder\file2.cs*folder/file2.cs
SRCSRV: end ------------------------------------------------".GetLines());
                yield return new TestCaseData(new HttpSourceServerDescriptor(
                            50,
                            "",
                            "",
                            new SourceFileDescriptor[0]))
                            .Returns(@"SRCSRV: ini ------------------------------------------------
VERSION=50
SRCSRV: variables ------------------------------------------
SRCSRVVERCTRL=
SRCSRVTRG=
SRCSRV: source files ---------------------------------------
SRCSRV: end ------------------------------------------------".GetLines());
            }
        }

        [TestCaseSource(nameof(ToStringTestCases))]
        public static IEnumerable<string> GivenSourceServerDataObject_WhenCallingToString_ThenAProperRawValueIGenerated(HttpSourceServerDescriptor descriptor)
        {
            return descriptor.ToString().GetLines();
        }

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
                        .SetName("Tree assemblies, same root web server, same version control, same version.");
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
                        .SetName("Two assemblies, same root web server, same version control, different version.");
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
                        .SetName("Two assemblies, different version control");
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
            var merged = primary.MergeWith(other);
            Assert.That(merged.Version, Is.EqualTo(expectedVersion));
            Assert.That(merged.VersionControl, Is.EqualTo(expectedVersionControl));
            Assert.That(merged.Target, Is.EqualTo(expectedTarget));
            CollectionAssert.AreEquivalent(
                expectedSourceFiles,
                merged.SourceFiles.Select(file => file.Variables)
                    .ToDictionary(vars => vars.ElementAt(0), vars => vars.ElementAt(1)));
        }
    }
}
