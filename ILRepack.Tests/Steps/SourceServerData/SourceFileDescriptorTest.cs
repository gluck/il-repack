using System.Collections;
using ILRepacking.Steps.SourceServerData;
using NUnit.Framework;

namespace ILRepack.Tests.Steps.SourceServerData
{
    public static class SourceFileDescriptorTest
    {
        public static IEnumerable ParsingTestCases
        {
            get
            {
                yield return new TestCaseData(
                        @"C:\my_project_path\sub_folder\file.cs*sub_folder\file.cs",
                        @"C:\my_project_path\sub_folder\file.cs",
                        @"sub_folder\file.cs");
                yield return new TestCaseData(
                        @"C:\my_project_path\sub_folder\file.cs",
                        @"C:\my_project_path\sub_folder\file.cs",
                        "");
                yield return new TestCaseData(@"*sub_folder\file.cs", "", @"sub_folder\file.cs");
                yield return new TestCaseData("", "", "");
                yield return new TestCaseData("*", "", "");
                yield return new TestCaseData(null, "", "");
            }
        }

        [TestCaseSource(nameof(ParsingTestCases))]
        public static void GivenARawSourceFileValue_WhenParsing_ThenPathAndVariableShouldSetProperly(
            string raw,
            string expectedPath,
            string expectedVariable2)
        {
            var sourceFileDescriptor = SourceFileDescriptor.Parse(raw);
            Assert.That(sourceFileDescriptor.Path, Is.EqualTo(expectedPath));
            Assert.That(sourceFileDescriptor.Variable2, Is.EqualTo(expectedVariable2));
        }

        public static IEnumerable ToStringTestCases
        {
            get
            {
                yield return new TestCaseData("", "").Returns("*");
                yield return new TestCaseData(@"c:\whatever\folder\somefile.cs", "").Returns(@"c:\whatever\folder\somefile.cs*");
                yield return new TestCaseData("", @"folder\somefile.cs").Returns(@"*folder\somefile.cs");
                yield return new TestCaseData(@"c:\whatever\folder\somefile.cs", @"folder\somefile.cs").Returns(@"c:\whatever\folder\somefile.cs*folder\somefile.cs");
            }
        }

        [TestCaseSource(nameof(ToStringTestCases))]
        public static string GivenASourceFileObject_WhenCallingToString_ThenTheSourceFileRawValueShouldbeGenerated(string path, string variable2)
        {
            return new SourceFileDescriptor(path, variable2).ToString();
        }
    }
}
