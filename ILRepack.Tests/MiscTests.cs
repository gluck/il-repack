using System.IO;
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

        [Test]
        public void SimpleAPITest()
        {
            string exePath = typeof(MiscTests).Assembly.Location;
            string directory = Path.GetDirectoryName(exePath);
            string dllPath = Path.Combine(directory, "Mono.Cecil.dll");
            string mergedExePath = Path.Combine(directory, "SimpleApiTestOutput.dll");

            try
            {
                var repackOptions = new RepackOptions
                {
                    InputAssemblies = new[] { exePath, dllPath },
                    OutputFile = mergedExePath
                };

                var repacker = new ILRepacking.ILRepack(repackOptions);
                repacker.Repack();
            }
            finally
            {
                File.Delete(mergedExePath);
            }
        }

        [Test]
        public void InitializeDotnetRuntimeDirectoriesTest()
        {
            var resolver = new RepackAssemblyResolver();
            resolver.InitializeDotnetRuntimeDirectories();
        }
    }
}