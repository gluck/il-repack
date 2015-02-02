using System;
using System.Collections.Generic;
using System.Linq;
using ILRepacking;
using Moq;
using NUnit.Framework;

namespace ILRepack.Tests
{
    [TestFixture]
    class RepackOptionsTests
    {
        Mock<ILogger> repackLogger;
        Mock<ICommandLine> commandLine;
        Mock<IFile> file;
        RepackOptions options;

        [SetUp]
        public void SetUp()
        {
            repackLogger = new Mock<ILogger>();
            commandLine = new Mock<ICommandLine>();
            file = new Mock<IFile>();
            options = new RepackOptions(commandLine.Object, repackLogger.Object, file.Object);
        }

        [Test]
        public void WithAllowDuplicateResources__GetModifier__ReturnModifier()
        {
            commandLine.Setup(cmd => cmd.Modifier("allowduplicateresources")).Returns(true);
            options.Parse();
            Assert.AreEqual(true, options.AllowDuplicateResources);
        }

        [Test]
        public void WithHelpModifierQuestionMark__CallShouldShowUsage__ReturnTrue()
        {
            commandLine.Setup(cmd => cmd.HasNoOptions).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("?")).Returns(true);
            Assert.IsTrue(options.ShouldShowUsage());
        }

        [Test]
        public void WithHelpModifierHelp__CallShouldShowUsage__ReturnTrue()
        {
            commandLine.Setup(cmd => cmd.HasNoOptions).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("?")).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("help")).Returns(true);
            Assert.IsTrue(options.ShouldShowUsage());
        }

        [Test]
        public void WithHelpModifierh__CallShouldShowUsage__ReturnTrue()
        {
            commandLine.Setup(cmd => cmd.HasNoOptions).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("?")).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("help")).Returns(true);
            commandLine.Setup(cmd => cmd.Modifier("h")).Returns(true);
            Assert.IsTrue(options.ShouldShowUsage());
        }

        [Test]
        public void WithNoOptions_CallShouldShowUsage__ReturnTrue()
        {
            commandLine.Setup(cmd => cmd.Modifier("?")).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("help")).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("h")).Returns(false);
            commandLine.Setup(cmd => cmd.HasNoOptions).Returns(true);
            Assert.IsTrue(options.ShouldShowUsage());
        }

        [Test]
        public void WithOptions_CallShouldShowUsage__ReturnFalse()
        {
            Assert.IsFalse(options.ShouldShowUsage());
        }

        [Test]
        public void WithAllowDuplicateTypes_WithTypes__CallParse__DuplicateTypesAreSet()
        {
            string[] types = { "PlatformFixer", "ReflectionHelper" };
            commandLine.Setup(cmd => cmd.Options("allowdup")).Returns(types);
            options.Parse();
            CollectionAssert.AreEquivalent(types, options.AllowedDuplicateTypes.Values);
        }

        [Test]
        public void WithAllowDuplicateTypes_WithNamespaces__CallParse__NamespacesAreSet()
        {
            string[] namespaces = { "PlatformFixer.*", "ReflectionHelper.*" };
            var namespaceTypes = namespaces.Select(name => name.TrimEnd('.', '*'));
            commandLine.Setup(cmd => cmd.Options("allowdup")).Returns(namespaces);
            options.Parse();
            CollectionAssert.AreEquivalent(namespaceTypes, options.AllowedDuplicateNameSpaces);
        }

        [Test]
        public void WithAllowDuplicateTypes_WithNamespaces_WithTypes__CallParse__TypesAndNamespacesAreSet()
        {
            string[] types = { "ILogger", "ILRepack" };
            string[] namespaces = { "PlatformFixer.*", "ReflectionHelper.*" };
            string[] duplicateTypes = types.Concat(namespaces).ToArray();
            commandLine.Setup(cmd => cmd.Options("allowdup")).Returns(duplicateTypes);
            options.Parse();
            var namespaceTypes = namespaces.Select(name => name.TrimEnd('.', '*'));
            CollectionAssert.AreEquivalent(types, options.AllowedDuplicateTypes.Values);
            CollectionAssert.AreEquivalent(namespaceTypes, options.AllowedDuplicateNameSpaces);
        }

        [Test]
        public void WithModifierNDebug__Parse__DebugInfoFalseIsSet()
        {
            options.Parse();
            Assert.IsTrue(options.DebugInfo);
            commandLine.Setup(cmd => cmd.Modifier("ndebug")).Returns(true);
            options.Parse();
            Assert.IsFalse(options.DebugInfo);
        }

        [Test]
        public void WithOptionInternalize__Parse__ExcludeFileIsSet()
        {
            commandLine.Setup(cmd => cmd.HasOption("internalize")).Returns(true);
            const string excludeFileName = "ILogger";
            commandLine.Setup(cmd => cmd.Option("internalize")).Returns(excludeFileName);
            options.Parse();
            Assert.AreEqual(excludeFileName, options.ExcludeFile);
        }

        [Test]
        public void WithOptionLog__Parse__LogFileIsSet()
        {
            commandLine.Setup(cmd => cmd.HasOption("log")).Returns(true);
            const string logFileName = "31012015.log";
            commandLine.Setup(cmd => cmd.Option("log")).Returns(logFileName);
            options.Parse();
            Assert.AreEqual(logFileName, options.LogFile);
        }

        [Test]
        public void WithOptionTargetKindLibrary__Parse__TargetKindIsSet()
        {
            commandLine.Setup(cmd => cmd.Option("target")).Returns("library");
            options.Parse();
            Assert.AreEqual(ILRepacking.ILRepack.Kind.Dll, options.TargetKind);
        }

        [Test]
        public void WithOptionTargetKindEXE__Parse__TargetKindIsSet()
        {
            commandLine.Setup(cmd => cmd.Option("target")).Returns("exe");
            options.Parse();
            Assert.AreEqual(ILRepacking.ILRepack.Kind.Exe, options.TargetKind);
        }

        [Test]
        public void WithOptionTargetKindWinEXE__Parse__TargetKindIsSet()
        {
            commandLine.Setup(cmd => cmd.Option("target")).Returns("winexe");
            options.Parse();
            Assert.AreEqual(ILRepacking.ILRepack.Kind.WinExe, options.TargetKind);
        }

        [Test]
        [ExpectedException(typeof(RepackOptions.InvalidTargetKindException))]
        public void WithOptionTargetKindInvalid__Parse__TargetKindIsSet()
        {
            commandLine.Setup(cmd => cmd.Option("target")).Returns("notsupportedtype");
            options.Parse();
        }

        [Test]
        public void WithOptionTargetPlatform__Parse__DirectoryAndVersionAreSet()
        {
            const string directory = "dir";
            const string version = "v1";
            var targetPlatform = string.Join(",", version, directory);
            commandLine.Setup(cmd => cmd.Option("targetplatform")).Returns(targetPlatform);
            options.Parse();
            Assert.AreEqual(directory, options.TargetPlatformDirectory);
            Assert.AreEqual(version, options.TargetPlatformVersion);
        }

        [Test]
        public void WithOptionTargetPlatform__Parse__VersionIsSet()
        {
            const string version = "v1";
            commandLine.Setup(cmd => cmd.Option("targetplatform")).Returns(version);
            options.Parse();
            Assert.AreEqual(version, options.TargetPlatformVersion);
        }

        [TestCase("v1")]
        [TestCase("v1.1")]
        [TestCase("v2")]
        [TestCase("v4")]
        public void WithModifierTargetVersion__Parse__TargetPlatformVersionIsSet(string version)
        {
            commandLine.Setup(cmd => cmd.Modifier(version)).Returns(true);
            options.Parse();
            Assert.AreEqual(version, options.TargetPlatformVersion);
        }

        [Test]
        public void WithOptionVersion__Parse__VersionIsSet()
        {
            var version = new Version("1.1");
            commandLine.Setup(cmd => cmd.Option("ver")).Returns(version.ToString());
            options.Parse();
            Assert.AreEqual(version, options.Version);
        }

        [Test]
        public void WithOptionKeyFileNotSet_WithDelaySign__Parse__Warn()
        {
            commandLine.Setup(cmd => cmd.Modifier("delaysign")).Returns(true);
            options.Parse();
            repackLogger.Verify(logger => logger.WARN(It.IsAny<string>()));
        }

        [Test]
        public void WithAllowMultipleAssign_WithNoCopyAttributes__Parse__Warn()
        {
            commandLine.Setup(cmd => cmd.Modifier("allowmultiple")).Returns(true);
            options.Parse();
            repackLogger.Verify(logger => logger.WARN(It.IsAny<string>()));
        }

        [Test]
        public void WithAttributeFile_WithCopyAttributes__Parse__Warn()
        {
            const string attributeFile = "filename";
            commandLine.Setup(cmd => cmd.Option("attr")).Returns(attributeFile);
            commandLine.Setup(cmd => cmd.Modifier("copyattrs")).Returns(true);
            options.Parse();
            repackLogger.Verify(logger => logger.WARN(It.IsAny<string>()));
        }
        
        [Test]
        public void WithNoSetup__SetSearchDirectories__SetGlobalAssemblyResolver()
        {
            var dirs = new List<string> { "dir1", "dir2", "dir3" };
            options.SetSearchDirectories(dirs.ToArray());
            var searchDirs = dirs.Concat(new string[] { ".", "bin"});
            CollectionAssert.AreEquivalent(searchDirs, options.GlobalAssemblyResolver.GetSearchDirectories());
        }

        [Test]
        public void WithNoSetup__SetTargetPlatform__TargetPlatformIsSet()
        {
            const string directory = "dir";
            const string version = "v1";
            options.SetTargetPlatform(version, directory);
            Assert.AreEqual(directory, options.TargetPlatformDirectory);
            Assert.AreEqual(version, options.TargetPlatformVersion);
        }
        
        [Test]
        [ExpectedException(typeof(ArgumentException), ExpectedMessage = "No output file given.")]
        public void WithNoOutputFile__ParseProperties__ThrowException()
        {
            options.ParseProperties();
        }

        [Test]
        [ExpectedException(typeof(ArgumentException), ExpectedMessage = "No input files given.")]
        public void WithNoInputAssemblies__ParseProperties__ThrowException()
        {
            commandLine.Setup(cmd => cmd.Option("out")).Returns("filename");
            options.Parse();
            Assert.IsNotNullOrEmpty(options.OutputFile);
            options.ParseProperties();
        }

        [Test]
        [ExpectedException(typeof(ArgumentException), ExpectedMessage = "KeyFile does not exist", MatchType = MessageMatch.Contains)]
        public void WithNoKeyFile__ParseProperties__ThrowException()
        {
            var inputAssemblies = new List<string> { "A", "B", "C" };
            commandLine.Setup(cmd => cmd.Option("out")).Returns("filename");
            commandLine.Setup(cmd => cmd.OtherAguments).Returns(inputAssemblies.ToArray());
            commandLine.Setup(cmd => cmd.Option("keyfile")).Returns("filename");
            options.Parse();
            Assert.IsNotNull(options.InputAssemblies);
            Assert.IsNotEmpty(options.InputAssemblies);
            options.ParseProperties();
        }

        [Test]
        public void WithNoKeyFile__ParseProperties__ReadExcludeInternalizedMatches()
        {
            var inputAssemblies = new List<string> { "A", "B", "C" };
            const string keyFile = "keyfilepath";
            var keyFileLines = new List<string> { "key1" };
            commandLine.Setup(cmd => cmd.Option("out")).Returns("outfilepath");
            commandLine.Setup(cmd => cmd.OtherAguments).Returns(inputAssemblies.ToArray());
            commandLine.Setup(cmd => cmd.HasOption("internalize")).Returns(true);
            commandLine.Setup(cmd => cmd.Option("internalize")).Returns(keyFile);
            file.Setup(_ => _.Exists(keyFile)).Returns(true);
            file.Setup(_ => _.ReadAllLines(keyFile)).Returns(keyFileLines.ToArray());
            options.Parse();
            Assert.IsNotNull(options.InputAssemblies);
            Assert.IsNotEmpty(options.InputAssemblies);
            options.ParseProperties();
            var pattern = options.ExcludeInternalizeMatches.First();
            Assert.IsTrue(pattern.IsMatch(keyFileLines.First()));
        }

    }
}
