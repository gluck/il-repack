using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ILRepacking;
using Moq;
using NUnit.Framework;

namespace ILRepack.Tests
{
    [TestFixture]
    class ILRepackTests
    {
        private RepackOptions repackOptions;
        private ILRepacking.ILRepack repack;
        private Mock<ILogger> logger;
        private Mock<ICommandLine> commandLine;
        private Mock<IFile> file;
        private List<string> inputAssembliesPath;

        [SetUp]
        public void SetUp()
        {
            logger = new Mock<ILogger>();
            commandLine = new Mock<ICommandLine>();
            file = new Mock<IFile>();

            repackOptions = new RepackOptions(commandLine.Object, logger.Object, file.Object);
            repack = new ILRepacking.ILRepack(repackOptions, logger.Object);

            var inputAssemblies = new List<string> { "AvalonDock.dll", 
                "ICsharpCode.AvalonEdit.dll", 
                "ICSharpCode.Build.Tasks.dll", 
                "ICSharpCode.Core.Presentation.dll",
                "ICSharpCode.Core.WinForms.dll",
                "ICSharpCode.Core.dll",
                "ICSharpCode.NRefactory.CSharp.Refactoring.dll",
                "ICSharpCode.NRefactory.CSharp.dll",
                "ICSharpCode.NRefactory.Cecil.dll",
                "ICSharpCode.NRefactory.Xml.dll",
                "ICSharpCode.NRefactory.dll",
                "ICSharpCode.SharpDevelop.Widgets.dll",
                "ICSharpCode.SharpDevelop.dll",
                "ICSharpCode.TreeView.dll",
                "Mono.Cecil.dll",
                "log4net.dll" };
            var dir = "../../sharpdevelop/";
            inputAssembliesPath = new List<string>();
            foreach (var assembly in inputAssemblies)
            {
                inputAssembliesPath.Add(dir + assembly);
            }
        }

        [Test]
        public void TestReadAssemblies()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            repackOptions.InputAssemblies = inputAssembliesPath.ToArray();

            repack.ReadInputAssemblies();
            stopwatch.Stop();
            Console.WriteLine("Read assemblies: " + stopwatch.ElapsedMilliseconds);
        }

        [Test]
        public void TestRepackReferences()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            repackOptions.InputAssemblies = inputAssembliesPath.ToArray();

            repack.ReadInputAssemblies();
            repack.RepackReferences();
            stopwatch.Stop();
            Console.WriteLine("Read assemblies + repack references: " + stopwatch.ElapsedMilliseconds);
        }
    }
}
