using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            CommandLine commandLine = new CommandLine(arguments);
            Assert.IsEmpty(commandLine.OtherAguments);
        }

        [Test]
        public void WithEmptyOption__GetOption__ReturnNull()
        {
            string[] arguments = { "" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("");
            Assert.IsNull(option);
        }

        [Test]
        public void WithInvalidOptionPrefix__GetOption__ReturnNull()
        {
            string[] arguments = { "&ver" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("ver");
            Assert.IsNull(option);
        }

        [Test]
        public void WithValidOptionPrefix__GetOption__ReturnNull()
        {
            string[] arguments = { "--keyfile:file", "--log" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("keyfile");
            Assert.AreEqual("file", option);
        }

        [Test]
        public void WithValidOption__GetOptionWithNameInCaps__ReturnOption()
        {
            string[] arguments = { "/keyfile:file" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("KEYFILE");
            Assert.AreEqual("file", option);
        }

        [Test]
        public void WithValidOption__GetOtherOption__ReturnNull()
        {
            string[] arguments = { "/keyfile:file" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("union");
            Assert.IsNull(option);
        }

        [Test]
        public void WithEmptyOption__GetOption__ReturnEmptyString()
        {
            string[] arguments = { "/" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("");
            Assert.IsEmpty(option);
        }

        [Test]
        public void WithOptions__RemoveOption__ReturnAllOptionsExceptTheOneRemoved()
        {
            string[] arguments = { "/var", "-var", "--var", "/log" };
            CommandLine commandLine = new CommandLine(arguments);
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
            CommandLine commandLine = new CommandLine(arguments);
            bool defaultValue = false;
            bool shouldUnion = commandLine.OptionBoolean("union", defaultValue);
            Assert.IsTrue(shouldUnion);
        }

        [Test]
        public void WithOptions__RemoveOptions__ReturnMatchedOptions()
        {
            string[] arguments = { "/union:true", "/log:file", "/var:3.3" };
            CommandLine commandLine = new CommandLine(arguments);
            string[] matchedArguments = commandLine.Options("union");
            Assert.AreEqual(1, matchedArguments.Length);
        }

        [Test]
        public void WithOptionWithInvalidAssignChar__GetOption__ReturnNull()
        {
            string[] arguments = { "/log-file" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("log");
            Assert.IsNull(option);
        }

        [Test]
        public void WithOneOption__NewObject__ReturnOption()
        {
            string versionOptionName = "ver";
            string versionNumber = "3.2.2.2";
            string[] arguments = { String.Format("/{0}:{1}", versionOptionName, versionNumber)};
            CommandLine commandLine = new CommandLine(arguments);
            Assert.IsNotEmpty(arguments);
            Assert.IsTrue(commandLine.HasOption(versionOptionName));
            string versionOption = commandLine.Option(versionOptionName);
            Assert.AreEqual(versionOption, versionNumber);
        }

        [Test]
        public void WithArguments__GetCount__ReturnCount(string argsCount)
        {
            string[] arguments = { "/var", "/log-file" };
            CommandLine commandLine = new CommandLine(arguments);
            Assert.AreEqual(2, commandLine.OptionsCount);
            Assert.IsFalse(commandLine.HasNoOptions);
        }

        [Test]
        public void WithZeroArguments__GetIsEmpty__ReturnTrue()
        {
            string[] arguments = { };
            CommandLine commandLine = new CommandLine(arguments);
            Assert.IsTrue(commandLine.HasNoOptions);
        }
    }
}
