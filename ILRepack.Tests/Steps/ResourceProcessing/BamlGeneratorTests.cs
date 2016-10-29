using Confuser.Renamer.BAML;
using ILRepack.Tests.Utils;
using ILRepacking;
using ILRepacking.Steps.ResourceProcessing;
using Mono.Cecil;
using Moq;
using NUnit.Framework;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Application = System.Windows.Application;

namespace ILRepack.Tests.Steps.ResourceProcessing
{
    [TestFixture]
    internal class BamlGeneratorTests
    {
        [Test]
        public void GenerateThemesGenericXaml_GivenIncludedXamlFiles_GeneratesSameBamlAsSample()
        {
            var expectedBamlDocument = GetResourceBamlDocument("Sample.xaml");
            var bamlGenerator = CreateBamlGenerator();

            var actualBamlDocument = bamlGenerator.GenerateThemesGenericXaml(new[]
            {
                "ButtonStyles.xaml",
                "TextBlockStyles.xaml"
            });

            AssertDocumentsAreEquivalent(actualBamlDocument, expectedBamlDocument);
        }

        [Test]
        public void AddMergedDictionaries_GivenExistingGenericXamlThatIsNotAResourceDictionary_LogsErrorAndReturnsSameBaml()
        {
            var initialDocument = GetResourceBamlDocument("NonResourceDictionary.xaml");
            var modifiedDocument = GetResourceBamlDocument("NonResourceDictionary.xaml");
            var logger = new Mock<ILogger>();
            var bamlGenerator = CreateBamlGenerator(logger.Object);

            bamlGenerator.AddMergedDictionaries(modifiedDocument, new[]
            {
                "ButtonStyles.xaml",
                "TextBlockStyles.xaml"
            });

            Assert.That(modifiedDocument, new BamlDocumentMatcher(initialDocument));
            logger.Verify(l => l.Error("Existing 'Themes/generic.xaml' in ClassLibrary is *not* a ResourceDictionary. " +
                                       "This will prevent proper WPF application merging."));
        }

        public IEnumerable GetExistingGenericXamlTestCases()
        {
            string[] testCases =
            {
                "EmptyResourceDictionary",
                "NonExistingMergedDictionaries",
                "ExistingMergedDictionaries"
            };

            return testCases.Select(testCase =>
                new TestCaseData(testCase + "/Start.xaml", testCase + "/End.xaml"));
        }

        [Test]
        [TestCaseSource("GetExistingGenericXamlTestCases")]
        public void AddMergedDictionaries_GivenExistingGenericXaml_CreatesExpectedXaml(
            string startingGenericXaml, string endResultGenericXaml)
        {
            var initialBamlDocument = GetResourceBamlDocument(startingGenericXaml);
            var bamlGenerator = CreateBamlGenerator();

            bamlGenerator.AddMergedDictionaries(initialBamlDocument, new[]
            {
                "ButtonStyles.xaml",
                "TextBlockStyles.xaml"
            });

            var expectedBamlDocument = GetResourceBamlDocument(endResultGenericXaml);
            AssertDocumentsAreEquivalent(initialBamlDocument, expectedBamlDocument);
        }

        private static void AssertDocumentsAreEquivalent(BamlDocument actualDocument, BamlDocument expectedBamlDocument)
        {
            Assert.That(actualDocument, new BamlDocumentMatcher(expectedBamlDocument));

            // Verify that we can parse the generated document
            byte[] actualDocumentBytes = BamlUtils.ToResourceBytes(actualDocument);

            BamlDocument newDocument = BamlUtils.FromResourceBytes(actualDocumentBytes);
            Assert.That(newDocument, new BamlDocumentMatcher(expectedBamlDocument));
        }

        private static BamlDocument GetResourceBamlDocument(string filename)
        {
            Application.ResourceAssembly = Assembly.GetExecutingAssembly();

            var streamInfo = Application.GetResourceStream(new Uri("/Resources/BamlGeneration/GenericXaml/" + filename, UriKind.Relative));
            var expectedBamlDocument = BamlReader.ReadDocument(streamInfo.Stream);

            return expectedBamlDocument;
        }

        private BamlGenerator CreateBamlGenerator(ILogger logger = null)
        {
            var mainAssembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("ClassLibrary", Version.Parse("1.0")),
                "CLassLibrary", ModuleKind.Dll);

            var references = new Mono.Collections.Generic.Collection<AssemblyNameReference>
            {
                AssemblyNameReference.Parse("WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"),
                AssemblyNameReference.Parse("PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"),
                AssemblyNameReference.Parse("PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")
            };

            return new BamlGenerator(logger ?? Mock.Of<ILogger>(), references, mainAssembly);
        }
    }
}
