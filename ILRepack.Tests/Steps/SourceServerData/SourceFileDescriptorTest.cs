using System.Collections;
using System.Collections.Generic;
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
                yield return new TestCaseData(@"C:\my_project_path\sub_folder\file.cs*sub_folder\file.cs")
                    .Returns(new[] { @"C:\my_project_path\sub_folder\file.cs", @"sub_folder\file.cs" });
                yield return new TestCaseData(@"C:\my_project_path\sub_folder\file.cs")
                    .Returns(new[] { @"C:\my_project_path\sub_folder\file.cs" });
                yield return new TestCaseData(@"*sub_folder\file.cs")
                    .Returns(new[] { "", @"sub_folder\file.cs" });
                yield return new TestCaseData("").Returns(new[] { "" });
                yield return new TestCaseData("*").Returns(new[] { "", "" });
                yield return new TestCaseData(null).Returns(new[] { "" });
                yield return new TestCaseData(@"C:\my_project_path\sub_folder\file.cs*sub_folder\file.cs*value3")
                    .Returns(new[] { @"C:\my_project_path\sub_folder\file.cs", @"sub_folder\file.cs", "value3" });
                yield return new TestCaseData("1*2*3*4*5*6*7*8*9*10*11").Returns(
                        new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11" });
            }
        }

        [TestCaseSource(nameof(ParsingTestCases))]
        public static IEnumerable<string> GivenARawSourceFileValue_WhenParsing_ThenVaraiblesShouldBeSetProperly(string raw)
        {
            return SourceFileDescriptor.Parse(raw).Variables;
        }

        public static IEnumerable ToStringTestCases
        {
            get
            {
                yield return new TestCaseData(new[] { new[] { "", "" } }).Returns("*");
                yield return new TestCaseData(new[] { new[] { @"c:\whatever\folder\somefile.cs", "" } })
                        .Returns(@"c:\whatever\folder\somefile.cs*");
                yield return new TestCaseData(new[] { new[] { "", @"folder\somefile.cs" } })
                        .Returns(@"*folder\somefile.cs");
                yield return new TestCaseData(new[] { new[] { @"c:\whatever\folder\somefile.cs", @"folder\somefile.cs" } })
                        .Returns(@"c:\whatever\folder\somefile.cs*folder\somefile.cs");
                yield return new TestCaseData(new[] { new[] { "1", "2", "3", "4", "5", "6", } })
                        .Returns("1*2*3*4*5*6");
            }
        }

        [TestCaseSource(nameof(ToStringTestCases))]
        public static string GivenASourceFileObject_WhenCallingToString_ThenTheSourceFileRawValueShouldbeGenerated(IEnumerable<string> variables)
        {
            return new SourceFileDescriptor(variables).ToString();
        }
    }
}
