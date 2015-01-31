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

        [SetUp]
        public void SetUp()
        {
            repackLogger = new Mock<ILogger>();
            commandLine = new Mock<ICommandLine>();
        }
        


    }
}
