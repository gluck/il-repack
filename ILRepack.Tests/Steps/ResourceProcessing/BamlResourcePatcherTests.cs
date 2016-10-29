using ILRepacking.Steps.ResourceProcessing;
using NUnit.Framework;

namespace ILRepack.Tests.Steps.ResourceProcessing
{
    internal class BamlResourcePatcherTests
    {
        [TestCase(
            "ClassLibrary.GenericBasedThemeResourceKey",
            ExpectedResult = "ClassLibrary.GenericBasedThemeResourceKey")]
        [TestCase(
            "ClassLibrary.GenericResourceKey`1[[ClassLibrary.ThemesResourceKey, ClassLibrary, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]",
            ExpectedResult = "ClassLibrary.GenericResourceKey`1[[ClassLibrary.ThemesResourceKey]]")]
        public string RemoveTypeAssemblyInformation_GivenFullTypeName_AssemblySpecificDataIsRemoved(string fullTypeName)
        {
            return BamlResourcePatcher.RemoveTypeAssemblyInformation(fullTypeName);
        }
    }
}
