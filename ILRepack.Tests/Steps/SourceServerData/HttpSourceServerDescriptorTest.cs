using System.Collections;
using System.Text;

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
                    3);
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
                    0);
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
            var success = HttpSourceServerDescriptor.TryParse(Encoding.UTF8.GetBytes(raw), out descriptor);
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
            var success = HttpSourceServerDescriptor.TryParse(Encoding.UTF8.GetBytes(raw), out descriptor);
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
SRCSRV: end ------------------------------------------------");
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
SRCSRV: end ------------------------------------------------");
            }
        }

        [TestCaseSource(nameof(ToStringTestCases))]
        public static string GivenSourceServerDataObject_WhenCallingToString_ThenAProperRawValueIGenerated(HttpSourceServerDescriptor descriptor)
        {
            return descriptor.ToString();
        }
    }
}
