using ILRepacking;
using NUnit.Framework;

namespace ILRepack.Tests
{
    public class MiscTests
    {
        [Test]
        public void ProcessRunnerTest()
        {
            var runtimes = ProcessRunner.Run("dotnet", "--list-runtimes");
            var output = runtimes.Output;
            Assert.True(output.Contains("Microsoft.NETCore.App"));
        }
    }
}