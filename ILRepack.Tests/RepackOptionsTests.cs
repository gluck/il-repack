using ILRepacking;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
        }

        void Parse()
        {
            options = new RepackOptions(commandLine.Object, file.Object);
        }

        [Test]
        public void WithAllowDuplicateResources__GetModifier__ReturnModifier()
        {
            commandLine.Setup(cmd => cmd.Modifier("allowduplicateresources")).Returns(true);
            Parse();
            Assert.AreEqual(true, options.AllowDuplicateResources);
        }

        [Test]
        public void WithHelpModifierQuestionMark__CallShouldShowUsage__ReturnTrue()
        {
            commandLine.Setup(cmd => cmd.HasNoOptions).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("?")).Returns(true);
            Parse();
            Assert.IsTrue(options.ShouldShowUsage);
        }

        [Test]
        public void WithHelpModifierHelp__CallShouldShowUsage__ReturnTrue()
        {
            commandLine.Setup(cmd => cmd.HasNoOptions).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("?")).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("help")).Returns(true);
            Parse();
            Assert.IsTrue(options.ShouldShowUsage);
        }

        [Test]
        public void WithHelpModifierh__CallShouldShowUsage__ReturnTrue()
        {
            commandLine.Setup(cmd => cmd.HasNoOptions).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("?")).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("help")).Returns(true);
            commandLine.Setup(cmd => cmd.Modifier("h")).Returns(true);
            Parse();
            Assert.IsTrue(options.ShouldShowUsage);
        }

        [Test]
        public void WithNoOptions_CallShouldShowUsage__ReturnTrue()
        {
            commandLine.Setup(cmd => cmd.Modifier("?")).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("help")).Returns(false);
            commandLine.Setup(cmd => cmd.Modifier("h")).Returns(false);
            commandLine.Setup(cmd => cmd.HasNoOptions).Returns(true);
            Parse();
            Assert.IsTrue(options.ShouldShowUsage);
        }

        [Test]
        public void WithOptions_CallShouldShowUsage__ReturnFalse()
        {
            Parse();
            Assert.IsFalse(options.ShouldShowUsage);
        }

        [Test]
        public void WithAllowDuplicateTypes_WithTypes__CallParse__DuplicateTypesAreSet()
        {
            string[] types = { "PlatformFixer", "ReflectionHelper" };
            commandLine.Setup(cmd => cmd.Options("allowdup")).Returns(types);
            Parse();
            CollectionAssert.AreEquivalent(types, options.AllowedDuplicateTypes.Values);
        }

        [Test]
        public void WithAllowDuplicateTypes_WithNamespaces__CallParse__NamespacesAreSet()
        {
            string[] namespaces = { "PlatformFixer.*", "ReflectionHelper.*" };
            var namespaceTypes = namespaces.Select(name => name.TrimEnd('.', '*'));
            commandLine.Setup(cmd => cmd.Options("allowdup")).Returns(namespaces);
            Parse();
            CollectionAssert.AreEquivalent(namespaceTypes, options.AllowedDuplicateNameSpaces);
        }

        [Test]
        public void WithAllowDuplicateTypes_WithNamespaces_WithTypes__CallParse__TypesAndNamespacesAreSet()
        {
            string[] types = { "ILogger", "ILRepack" };
            string[] namespaces = { "PlatformFixer.*", "ReflectionHelper.*" };
            string[] duplicateTypes = types.Concat(namespaces).ToArray();
            commandLine.Setup(cmd => cmd.Options("allowdup")).Returns(duplicateTypes);
            Parse();
            var namespaceTypes = namespaces.Select(name => name.TrimEnd('.', '*'));
            CollectionAssert.AreEquivalent(types, options.AllowedDuplicateTypes.Values);
            CollectionAssert.AreEquivalent(namespaceTypes, options.AllowedDuplicateNameSpaces);
        }

        [Test]
        public void WithModifierNDebug__Parse__DebugInfoFalseIsSet()
        {
            Parse();
            Assert.IsTrue(options.DebugInfo);
            commandLine.Setup(cmd => cmd.Modifier("ndebug")).Returns(true);
            Parse();
            Assert.IsFalse(options.DebugInfo);
        }

        [Test]
        public void WithOptionInternalize__Parse__ExcludeFileIsSet()
        {
            commandLine.Setup(cmd => cmd.HasOption("internalize")).Returns(true);
            const string excludeFileName = "ILogger";
            commandLine.Setup(cmd => cmd.Option("internalize")).Returns(excludeFileName);
            Parse();
            Assert.AreEqual(excludeFileName, options.ExcludeFile);
        }

        [Test]
        public void WithOptionLog__Parse__LogFileIsSet()
        {
            commandLine.Setup(cmd => cmd.HasOption("log")).Returns(true);
            const string logFileName = "31012015.log";
            commandLine.Setup(cmd => cmd.Option("log")).Returns(logFileName);
            Parse();
            Assert.AreEqual(logFileName, options.LogFile);
        }

        [Test]
        public void WithOptionTargetKindLibrary__Parse__TargetKindIsSet()
        {
            commandLine.Setup(cmd => cmd.Option("target")).Returns("library");
            Parse();
            Assert.AreEqual(ILRepacking.ILRepack.Kind.Dll, options.TargetKind);
        }

        [Test]
        public void WithOptionTargetKindEXE__Parse__TargetKindIsSet()
        {
            commandLine.Setup(cmd => cmd.Option("target")).Returns("exe");
            Parse();
            Assert.AreEqual(ILRepacking.ILRepack.Kind.Exe, options.TargetKind);
        }

        [Test]
        public void WithOptionTargetKindWinEXE__Parse__TargetKindIsSet()
        {
            commandLine.Setup(cmd => cmd.Option("target")).Returns("winexe");
            Parse();
            Assert.AreEqual(ILRepacking.ILRepack.Kind.WinExe, options.TargetKind);
        }

        [Test]
        [ExpectedException(typeof(RepackOptions.InvalidTargetKindException))]
        public void WithOptionTargetKindInvalid__Parse__TargetKindIsSet()
        {
            commandLine.Setup(cmd => cmd.Option("target")).Returns("notsupportedtype");
            Parse();
        }

        [Test]
        public void WithOptionTargetPlatform__Parse__DirectoryAndVersionAreSet()
        {
            const string directory = "dir";
            const string version = "v2";
            var targetPlatform = string.Join(",", version, directory);
            commandLine.Setup(cmd => cmd.Option("targetplatform")).Returns(targetPlatform);
            Parse();
            Assert.AreEqual(directory, options.TargetPlatformDirectory);
            Assert.AreEqual(version, options.TargetPlatformVersion);
        }

        [Test]
        public void WithOptionTargetPlatform__Parse__VersionIsSet()
        {
            const string version = "v2";
            commandLine.Setup(cmd => cmd.Option("targetplatform")).Returns(version);
            Parse();
            Assert.AreEqual(version, options.TargetPlatformVersion);
        }

        [TestCase("v2")]
        [TestCase("v4")]
        public void WithModifierTargetVersion__Parse__TargetPlatformVersionIsSet(string version)
        {
            commandLine.Setup(cmd => cmd.Modifier(version)).Returns(true);
            Parse();
            Assert.AreEqual(version, options.TargetPlatformVersion);
        }

        [Test]
        public void WithOptionVersion__Parse__VersionIsSet()
        {
            var version = new Version("1.1");
            commandLine.Setup(cmd => cmd.Option("ver")).Returns(version.ToString());
            Parse();
            Assert.AreEqual(version, options.Version);
        }

        [Test]
        public void WithOptionKeyFileNotSet_WithDelaySign__Parse__ThrowsInvalidOperationException()
        {
            commandLine.Setup(cmd => cmd.Modifier("delaysign")).Returns(true);
            Parse();
            Assert.Throws<InvalidOperationException>(() => options.Validate());
        }

        [Test]
        public void WithOptionKeyContainerSet_WithDelaySign__Parse__NoException()
        {
            commandLine.Setup(cmd => cmd.Option("out")).Returns("filename");
            commandLine.Setup(cmd => cmd.OtherAguments).Returns(new[] { "A", "B", "C" });
            commandLine.Setup(cmd => cmd.Option("keycontainer")).Returns("containername");
            commandLine.Setup(cmd => cmd.Modifier("delaysign")).Returns(true);
            Parse();
            options.Validate();
        }

        [Test]
        public void WithAllowMultipleAssign_WithNoCopyAttributes__Parse__ThrowsInvalidOperationException()
        {
            commandLine.Setup(cmd => cmd.Modifier("allowmultiple")).Returns(true);
            Parse();
            Assert.Throws<InvalidOperationException>(() => options.Validate());
        }

        [Test]
        public void WithAttributeFile_WithCopyAttributes__Parse__ThrowsInvalidOperationException()
        {
            const string attributeFile = "filename";
            commandLine.Setup(cmd => cmd.Option("attr")).Returns(attributeFile);
            commandLine.Setup(cmd => cmd.Modifier("copyattrs")).Returns(true);
            Parse();
            Assert.Throws<InvalidOperationException>(() => options.Validate());
        }

        [Test]
        [ExpectedException(typeof(ArgumentException), ExpectedMessage = "No input files given.")]
        public void WithNoInputAssemblies__ParseProperties__ThrowException()
        {
            commandLine.Setup(cmd => cmd.Option("out")).Returns("filename");
            Parse();
            options.Validate();
        }

        [Test]
        [ExpectedException(typeof(ArgumentException), ExpectedMessage = "KeyFile does not exist", MatchType = MessageMatch.Contains)]
        public void WithNoKeyFile__ParseProperties__ThrowException()
        {
            var inputAssemblies = new List<string> { "A", "B", "C" };
            commandLine.Setup(cmd => cmd.Option("out")).Returns("filename");
            commandLine.Setup(cmd => cmd.OtherAguments).Returns(inputAssemblies.ToArray());
            commandLine.Setup(cmd => cmd.Option("keyfile")).Returns("filename");
            Parse();
            options.Validate();
        }

        [Test]
        [ExpectedException(typeof(ArgumentException), ExpectedMessage = "KeyFile does not exist", MatchType = MessageMatch.Contains)]
        public void WithNoKeyFileEvenWithKeyContainer__ParseProperties__ThrowException()
        {
            var inputAssemblies = new List<string> { "A", "B", "C" };
            commandLine.Setup(cmd => cmd.Option("out")).Returns("filename");
            commandLine.Setup(cmd => cmd.OtherAguments).Returns(inputAssemblies.ToArray());
            commandLine.Setup(cmd => cmd.Option("keyfile")).Returns("filename");
            commandLine.Setup(cmd => cmd.Option("keycontainer")).Returns("containername");
            Parse();
            options.Validate();
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
            Parse();
            Assert.IsNotNull(options.InputAssemblies);
            Assert.IsNotEmpty(options.InputAssemblies);
            var pattern = options.ExcludeInternalizeMatches.First();
            Assert.IsTrue(pattern.IsMatch(keyFileLines.First()));
        }

        [Test]
        public void CanSetExcludeOptionsWithoutCommandLine()
        {
            Parse();
            var r = new Regex("test");
            options.ExcludeInternalizeMatches.Add(r);
            CollectionAssert.AreEqual(new[] { r }, options.ExcludeInternalizeMatches);
        }

        [Test]
        public void SettingExcludeFileReadsFromFile()
        {
            const string excludeFile = "excludefile";
            var excludeLines = new List<string> { "ex1", "ex2" };
            Parse();
            file.Setup(_ => _.ReadAllLines(excludeFile)).Returns(excludeLines.ToArray());
            options.ExcludeFile = excludeFile;
            Assert.AreEqual(excludeFile, options.ExcludeFile);
            CollectionAssert.AreEqual(excludeLines, options.ExcludeInternalizeMatches.Select(r => r.ToString()));
        }
                
        [TestCase(false)]
        [TestCase(true)]
        public void WithRenameInternalizedSet_WithInternalize__Parse__RenameInternalized_ShouldBeCorrect(bool renameInternalized)
        {
            commandLine.Setup(cmd => cmd.HasOption("internalize")).Returns(true);
            commandLine.Setup(cmd => cmd.Option("internalize")).Returns(string.Empty);
            commandLine.Setup(cmd => cmd.Modifier("renameinternalized")).Returns(renameInternalized);
            Parse();
            Assert.AreEqual(renameInternalized, options.RenameInternalized);
        }

        [Test]
        public void WithRenameInternalizedSet_WithInternalize__Parse__NoException()
        {
            var inputAssemblies = new List<string> { "A", "B", "C" };
            commandLine.Setup(cmd => cmd.Option("out")).Returns("filename");
            commandLine.Setup(cmd => cmd.OtherAguments).Returns(inputAssemblies.ToArray());
            
            commandLine.Setup(cmd => cmd.HasOption("internalize")).Returns(true);
            commandLine.Setup(cmd => cmd.Option("internalize")).Returns(string.Empty);
            commandLine.Setup(cmd => cmd.Modifier("renameinternalized")).Returns(true);
            Parse();
            options.Validate();
            Assert.DoesNotThrow(() => options.Validate());
        }

        [Test]
        public void WithRenameInternalizedSet_WithoutInternalize__Parse__ThrowsInvalidOperationException()
        {
            var inputAssemblies = new List<string> { "A", "B", "C" };
            commandLine.Setup(cmd => cmd.Option("out")).Returns("filename");
            commandLine.Setup(cmd => cmd.OtherAguments).Returns(inputAssemblies.ToArray());
            
            commandLine.Setup(cmd => cmd.Modifier("renameinternalized")).Returns(true);
            Parse();
            Assert.Throws<InvalidOperationException>(() => options.Validate());
        }
    }
}
