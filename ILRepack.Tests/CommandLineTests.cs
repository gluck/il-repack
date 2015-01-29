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
        public void WithNoArguments_NewObject_NoOtherArguments()
        {
            string[] arguments = { };
            CommandLine commandLine = new CommandLine(arguments);
            Assert.IsEmpty(commandLine.OtherAguments);
        }

        [Test]
        public void WithEmptyOption_GetOption_ReturnNull()
        {
            string[] arguments = { "" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("");
            Assert.IsNull(option);
        }

        [Test]
        public void WithInvalidOptionPrefix_GetOption_ReturnNull()
        {
            string[] arguments = { "&ver" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("ver");
            Assert.IsNull(option);
        }

        [Test]
        public void WithValidOptionPrefix_GetOption_ReturnNull()
        {
            string[] arguments = { "--keyfile:file", "--log" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("keyfile");
            Assert.AreEqual("file", option);
        }

        [Test]
        public void WithValidOption_GetOptionWithNameInCaps_ReturnOption()
        {
            string[] arguments = { "/keyfile:file" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("KEYFILE");
            Assert.AreEqual("file", option);
        }

        [Test]
        public void WithValidOption_GetOtherOption_ReturnNull()
        {
            string[] arguments = { "/keyfile:file" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("union");
            Assert.IsNull(option);
        }

        [Test]
        public void WithEmptyOption_GetOption_ReturnEmptyString()
        {
            string[] arguments = { "/" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("");
            Assert.IsEmpty(option);
        }

        [Test]
        public void WithOptions_RemoveOption_ReturnAllOptionsExceptTheOneRemoved()
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
        public void WithBoolOption_GetOption_ReturnOption()
        {
            string[] arguments = { "/union:true" };
            CommandLine commandLine = new CommandLine(arguments);
            bool defaultValue = false;
            bool shouldUnion = commandLine.OptionBoolean("union", defaultValue);
            Assert.IsTrue(shouldUnion);
        }

        [Test]
        public void WithOptions_RemoveOptions_ReturnMatchedOptions()
        {
            string[] arguments = { "/union:true", "/log:file", "/var:3.3" };
            CommandLine commandLine = new CommandLine(arguments);
            string[] matchedArguments = commandLine.Options("union");
            Assert.AreEqual(1, matchedArguments.Length);
        }

        [Test]
        public void WithOptionWithInvalidAssignChar_GetOption_ReturnNull()
        {
            string[] arguments = { "/log-file" };
            CommandLine commandLine = new CommandLine(arguments);
            string option = commandLine.Option("log");
            Assert.IsNull(option);
        }

        [Test]
        public void WithOneOption_NewObject_ReturnOption()
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

    }
}
