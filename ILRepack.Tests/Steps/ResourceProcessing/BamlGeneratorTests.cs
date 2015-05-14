using Confuser.Renamer.BAML;
using ILRepack.Tests.Utils;
using ILRepacking.Steps.ResourceProcessing;
using Mono.Cecil;
using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace ILRepack.Tests.Steps.ResourceProcessing
{
    [TestFixture]
    internal class BamlGeneratorTests
    {
        [Test]
        public void GenerateThemesGenericXaml_GivenIncludedXamlFiles_GeneratesSameBamlAsEmbeddedOne()
        {
            Application.ResourceAssembly = Assembly.GetExecutingAssembly();
            var streamInfo = Application.GetResourceStream(new Uri("/Resources/SampleThemesGeneric.xaml", UriKind.Relative));
            var expectedBamlDocument = BamlReader.ReadDocument(streamInfo.Stream);
            var bamlGenerator = CreateBamlGenerator();

            var actualBamlDocument = bamlGenerator.GenerateThemesGenericXaml(new[]
            {
                "ButtonStyles.baml",
                "TextBlockStyles.baml"
            });

            Assert.That(actualBamlDocument, new BamlDocumentMatcher(expectedBamlDocument));

            using (MemoryStream stream = new MemoryStream())
            {
                BamlWriter.WriteDocument(actualBamlDocument, stream);
                stream.Position = 0;

                // Verify we can parse the generated document
                var newDoc = BamlReader.ReadDocument(stream);
                Assert.That(newDoc, new BamlDocumentMatcher(expectedBamlDocument));
            }
        }

        private BamlGenerator CreateBamlGenerator()
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

            return new BamlGenerator(references, mainAssembly);
        }
    }
}
