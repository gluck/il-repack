using System;
using NUnit.Framework;
using ILRepacking;

namespace ILRepack.Tests
{
    class CommandLineTests
    {
        [Test]
        public void WithNoArguments__NewObject__NoOtherArguments()
        {
            string[] arguments = { };
            var commandLine = new CommandLine(arguments);
            Assert.IsEmpty(commandLine.OtherAguments);
        }

        [Test]
        public void WithEmptyOption__GetOption__ReturnNull()
        {
            string[] arguments = { "" };
            var commandLine = new CommandLine(arguments);
            string option = commandLine.Option("");
            Assert.IsNull(option);
        }

        [Test]
        public void WithInvalidOptionPrefix__GetOption__ReturnNull()
        {
            string[] arguments = { "&ver" };
            var commandLine = new CommandLine(arguments);
            var option = commandLine.Option("ver");
            Assert.IsNull(option);
        }

        [Test]
        public void WithValidOptionPrefix__GetOption__ReturnNull()
        {
            string[] arguments = { "--keyfile:file", "--log" };
            var commandLine = new CommandLine(arguments);
            var option = commandLine.Option("keyfile");
            Assert.AreEqual("file", option);
        }

        [Test]
        public void WithValidOption__GetOptionWithNameInCaps__ReturnOption()
        {
            string[] arguments = { "/keyfile:file" };
            var commandLine = new CommandLine(arguments);
            var option = commandLine.Option("KEYFILE");
            Assert.AreEqual("file", option);
        }

        [Test]
        public void WithValidOption__GetOtherOption__ReturnNull()
        {
            string[] arguments = { "/keyfile:file" };
            var commandLine = new CommandLine(arguments);
            var option = commandLine.Option("union");
            Assert.IsNull(option);
        }

        [Test]
        public void WithEmptyOption__GetOption__ReturnEmptyString()
        {
            string[] arguments = { "/" };
            var commandLine = new CommandLine(arguments);
            var option = commandLine.Option("");
            Assert.IsEmpty(option);
        }

        [Test]
        public void WithOptions__RemoveOption__ReturnAllOptionsExceptTheOneRemoved()
        {
            string[] arguments = { "/var", "-var", "--var", "/log" };
            var commandLine = new CommandLine(arguments);
            Assert.IsTrue(commandLine.Modifier("var"));
            Assert.IsFalse(commandLine.HasOption("var"));
            Assert.IsTrue(commandLine.Modifier("log"));
            Assert.IsFalse(commandLine.HasOption("log"));
            Assert.IsEmpty(commandLine.OtherAguments);
        }

        [Test]
        public void WithBoolOption__GetOption__ReturnOption()
        {
            string[] arguments = { "/union:true" };
            var commandLine = new CommandLine(arguments);
            const bool defaultValue = false;
            var shouldUnion = commandLine.OptionBoolean("union", defaultValue);
            Assert.IsTrue(shouldUnion);
        }

        [Test]
        public void WithOptions__RemoveOptions__ReturnMatchedOptions()
        {
            string[] arguments = { "/union:true", "/log:file", "/var:3.3" };
            var commandLine = new CommandLine(arguments);
            var matchedArguments = commandLine.Options("union");
            Assert.AreEqual(1, matchedArguments.Length);
        }

        [Test]
        public void WithOptionWithInvalidAssignChar__GetOption__ReturnNull()
        {
            string[] arguments = { "/log-file" };
            var commandLine = new CommandLine(arguments);
            var option = commandLine.Option("log");
            Assert.IsNull(option);
        }

        [Test]
        public void WithOneOption__NewObject__ReturnOption()
        {
            const string versionOptionName = "ver";
            const string versionNumber = "3.2.2.2";
            string[] arguments = { String.Format("/{0}:{1}", versionOptionName, versionNumber)};
            var commandLine = new CommandLine(arguments);
            Assert.IsNotEmpty(arguments);
            Assert.IsTrue(commandLine.HasOption(versionOptionName));
            var versionOption = commandLine.Option(versionOptionName);
            Assert.AreEqual(versionOption, versionNumber);
        }

        [Test]
        public void WithArguments__GetCount__ReturnCount()
        {
            string[] arguments = { "/var", "/log-file" };
            var commandLine = new CommandLine(arguments);
            Assert.AreEqual(2, commandLine.OptionsCount);
            Assert.IsFalse(commandLine.HasNoOptions);
        }

        [Test]
        public void WithZeroArguments__GetIsEmpty__ReturnTrue()
        {
            string[] arguments = { };
            var commandLine = new CommandLine(arguments);
            Assert.IsTrue(commandLine.HasNoOptions);
        }
    }
}
