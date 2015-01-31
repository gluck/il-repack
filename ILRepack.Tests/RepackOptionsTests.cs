using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        RepackOptions options;

        [SetUp]
        public void SetUp()
        {
            repackLogger = new Mock<ILogger>();
            commandLine = new Mock<ICommandLine>();
            options = new RepackOptions(commandLine.Object, repackLogger.Object);
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

    }
}
