using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using ILRepacking;
using Moq;
using NUnit.Framework;

namespace ILRepack.Tests
{
    [TestFixture]
    class RepackAssembliesTests
    {
        private RepackOptions options;
        private RepackAssemblies assemblies;
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

            options = new RepackOptions(commandLine.Object, logger.Object, file.Object);
            assemblies = new RepackAssemblies(options, logger.Object, file.Object);

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
            options.InputAssemblies = inputAssembliesPath.ToArray();
            options.DebugInfo = true;
            foreach (var path in options.InputAssemblies)
            {
                file.Setup(f => f.Exists(Path.ChangeExtension(path, "pdb"))).Returns(true);
                file.Setup(f => f.Exists(path)).Returns(true);
            }
            file.Setup(f => f.Exists("log4net.pdb")).Returns(false);
            file.Setup(f => f.Exists("log4net.dll.mdb")).Returns(false);
            logger.Setup(l => l.INFO(It.IsAny<string>())).Callback<string>(Console.WriteLine);

            assemblies.ReadInputAssemblies();
            stopwatch.Stop();
            Console.WriteLine("Read assemblies: " + stopwatch.ElapsedMilliseconds);
        }
    }
}
