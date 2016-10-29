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
        private IEnumerable GetReferencedAssemblyPatchPathTestData()
        {
            return new[]
            {
                new TestCaseData(
                    "pack://application:,,,/ClassLibrary;component/TextBlockStyles.xaml",
                    "pack://application:,,,/MainAssembly;component/ClassLibrary/TextBlockStyles.xaml"),

                new TestCaseData(
                    "/ClassLibrary;component/ButtonStyles.xaml",
                    "/MainAssembly;component/ClassLibrary/ButtonStyles.xaml"),

                new TestCaseData(
                    "/themes/ButtonStyles.xaml",
                    "/ClassLibrary/themes/ButtonStyles.xaml"),

                new TestCaseData(null, null),
                new TestCaseData(string.Empty, string.Empty),
                new TestCaseData("asdasd", "asdasd"),
                new TestCaseData("/lol", "/lol"),
                new TestCaseData("123", "123"),
                new TestCaseData("/ClassLibrary", "/ClassLibrary")
            };
        }

        [TestCaseSource(nameof(GetReferencedAssemblyPatchPathTestData))]
        public void PatchPath_GivenPathInReferencedAssembly_ReturnsExpectedPatchedPath(
            string inputPath, string expectedPatchedPath)
        {
            AssemblyDefinition mainAssemblyDefinition =
                AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition("MainAssembly", Version.Parse("1.0.0")), "MainAssembly", ModuleKind.Windows);

            AssemblyDefinition libraryDefinition =
                AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition("ClassLibrary", Version.Parse("1.0.0")), "ClassLibrary", ModuleKind.Dll);

            string actualPatchedPath = XamlResourcePathPatcherStep.PatchPath(
                inputPath, mainAssemblyDefinition, libraryDefinition, new List<AssemblyDefinition> { libraryDefinition });

            Assert.AreEqual(expectedPatchedPath, actualPatchedPath);
        }

        private IEnumerable GetMainAssemblyPatchPathTestData()
        {
            return new[]
            {
                new TestCaseData(
                    "pack://application:,,,/ClassLibrary;component/TextBlockStyles.xaml",
                    "pack://application:,,,/MainAssembly;component/ClassLibrary/TextBlockStyles.xaml"),

                new TestCaseData(
                    "/MainAssembly;component/ButtonStyles.xaml",
                    "/MainAssembly;component/ButtonStyles.xaml"),

                new TestCaseData(
                    "/ClassLibrary;component/ButtonStyles.xaml",
                    "/MainAssembly;component/ClassLibrary/ButtonStyles.xaml"),

                new TestCaseData(
                    "/themes/ButtonStyles.xaml",
                    "/themes/ButtonStyles.xaml"),

                new TestCaseData(null, null),
                new TestCaseData(string.Empty, string.Empty),
                new TestCaseData("asdasd", "asdasd"),
                new TestCaseData("/lol", "/lol"),
                new TestCaseData("123", "123"),
                new TestCaseData("/ClassLibrary", "/ClassLibrary")
            };
        }

        [TestCaseSource(nameof(GetMainAssemblyPatchPathTestData))]
        public void PatchPath_GivenPathInMainAssembly_ReturnsExpectedPatchedPath(
            string inputPath, string expectedPatchedPath)
        {
            AssemblyDefinition mainAssemblyDefinition =
                AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition("MainAssembly", Version.Parse("1.0.0")), "MainAssembly", ModuleKind.Windows);

            AssemblyDefinition libraryDefinition =
                AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition("ClassLibrary", Version.Parse("1.0.0")), "ClassLibrary", ModuleKind.Dll);

            string actualPatchedPath = XamlResourcePathPatcherStep.PatchPath(
                inputPath, mainAssemblyDefinition, mainAssemblyDefinition, new List<AssemblyDefinition> { libraryDefinition });

            Assert.AreEqual(expectedPatchedPath, actualPatchedPath);
        }
    }
}
