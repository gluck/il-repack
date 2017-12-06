using ILRepacking;
using ILRepacking.Steps.ResourceProcessing;
using Mono.Cecil;
using Moq;
using NUnit.Framework;
using System;

namespace ILRepack.Tests.Steps.ResourceProcessing
{
    internal class BamlResourcePatcherTests
    {
        [TestCase(
            "ClassLibrary.GenericBasedThemeResourceKey",
            ExpectedResult = "ClassLibrary.GenericBasedThemeResourceKey")]
        [TestCase(
            "ClassLibrary.GenericResourceKey`1[[ClassLibrary.ThemesResourceKey, ClassLibrary, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]",
            ExpectedResult = "ClassLibrary.GenericResourceKey`1[[ClassLibrary.ThemesResourceKey, MainApp, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]")]

        [TestCase(
            "DevExpress.Mvvm.UI.Interactivity.EventTriggerBase`1[[System.Windows.DependencyObject, WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35]]",
            ExpectedResult = "DevExpress.Mvvm.UI.Interactivity.EventTriggerBase`1[[System.Windows.DependencyObject, WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35]]")]
        public string RemoveTypeAssemblyInformation_GivenFullTypeName_AssemblySpecificDataIsRemoved(string fullTypeName)
        {
            var primaryAssembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("MainApp", new Version(1, 0, 0, 0)),
                "MainApp.exe", ModuleKind.Console);

            var otherAssemblies = new[]
            {
                AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition("ClassLibrary", new Version(1, 0)),
                    "ClassLibrary.dll", ModuleKind.Dll)
            };

            var repackContext = Mock.Of<IRepackContext>(c =>
                c.PrimaryAssemblyDefinition == primaryAssembly &&
                c.OtherAssemblies == otherAssemblies);
            var patcher = new BamlResourcePatcher(repackContext);

            return patcher.RemoveTypeAssemblyInformation(fullTypeName);
        }
    }
}
