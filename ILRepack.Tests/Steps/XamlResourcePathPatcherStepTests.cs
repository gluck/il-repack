using ILRepacking.Steps;
using Mono.Cecil;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;

namespace ILRepack.Tests.Steps
{
    [TestFixture]
    public class XamlResourcePathPatcherStepTests
    {
        private IEnumerable GetPatchPathTestData()
        {
            return new[]
            {
                new TestCaseData(
                    "pack://application:,,,/ClassLibrary;component/TextBlockStyles.xaml",
                    "pack://application:,,,/MainAssembly;component/ClassLibrary/TextBlockStyles.xaml"),

                new TestCaseData(
                    "/ClassLibrary;component/ButtonStyles.xaml",
                    "/MainAssembly;component/ClassLibrary/ButtonStyles.xaml"),

                new TestCaseData(null, null),
                new TestCaseData(string.Empty, string.Empty),
                new TestCaseData("asdasd", "asdasd"),
                new TestCaseData("/lol", "/lol"),
                new TestCaseData("123", "123"),
                new TestCaseData("/ClassLibrary", "/ClassLibrary")
            };
        }

        [TestCaseSource("GetPatchPathTestData")]
        public void PatchPath_GivenPath_ReturnsExpectedPatchedPath(
            string inputPath, string expectedPatchedPath)
        {
            AssemblyDefinition mainAssemblyDefinition =
                AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition("MainAssembly", Version.Parse("1.0.0")), "MainAssembly", ModuleKind.Windows);

            AssemblyDefinition libraryDefinition =
                AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition("ClassLibrary", Version.Parse("1.0.0")), "ClassLibrary", ModuleKind.Dll);

            string actualPatchedPath = XamlResourcePathPatcherStep.PatchPath(
                inputPath, mainAssemblyDefinition, new List<AssemblyDefinition> { libraryDefinition });

            Assert.AreEqual(expectedPatchedPath, actualPatchedPath);
        }
    }
}
