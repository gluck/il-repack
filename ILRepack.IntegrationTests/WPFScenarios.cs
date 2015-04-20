using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;

namespace ILRepack.IntegrationTests
{
    [TestFixture]
    public class WPFScenarios
    {
        private const int ScenarioProcessWaitTimeInMs = 10000;

        [Test]
        public void GivenXAMLThatUsesLibraryClass_MergedWPFApplicationRunsSuccessfully()
        {
            RunScenario("LibraryClassUsageInXAML");
        }

        private void RunScenario(string scenarioName)
        {
            string scenarioExecutable = GetScenarioExecutable(scenarioName);

            AssertFileExists(scenarioExecutable);

            var processStartInfo = new ProcessStartInfo(scenarioExecutable)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            Process process = Process.Start(processStartInfo);
            Assert.NotNull(process);

            bool processEnded = process.WaitForExit(ScenarioProcessWaitTimeInMs);
            Assert.That(processEnded, Is.True, "Process has not ended.");

            Console.WriteLine("\nScenario '{0}' STDOUT: {1}", scenarioName, process.StandardOutput.ReadToEnd());
            Assert.That(process.ExitCode, Is.EqualTo(0), "Process exited with error");
        }

        private string GetScenarioExecutable(string scenarioName)
        {
            string scenariosDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, @"..\..\Scenarios\");
            string scenarioDirectory = Path.Combine(scenariosDirectory, scenarioName);
            string scenarioExecutableFileName = scenarioName + ".exe";

            return Path.GetFullPath(Path.Combine(
                scenarioDirectory,
                "bin",
                GetRunningConfiguration(),
                "merged",
                scenarioExecutableFileName));
        }

        private static void AssertFileExists(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Assert.Fail("File '{0}' does not exist.", filePath);
            }
        }

        private string GetRunningConfiguration()
        {
#if DEBUG
            return "Debug";
#else
            return "Release";
#endif
        }
    }
}
