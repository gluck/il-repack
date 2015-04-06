using NUnit.Framework;
using System.Diagnostics;
using System.IO;

namespace ILRepack.IntegrationTests
{
    [TestFixture]
    public class WPFScenarios
    {
        private const int ScenarioProcessWaitTimeInMs = 2000;

        [Test]
        public void GivenXAMLThatUsesLibraryClass_MergedWPFApplicationRunsSuccessfully()
        {
            RunScenario("LibraryClassUsageInXAML");
        }

        private void RunScenario(string scenarioName)
        {
            string scenarioExecutable = GetScenarioExecutable(scenarioName);

            AssertFileExists(scenarioExecutable);

            Process process = Process.Start(scenarioExecutable);
            Assert.NotNull(process);

            bool processEnded = process.WaitForExit(ScenarioProcessWaitTimeInMs);
            Assert.IsTrue(processEnded);

            Assert.AreEqual(0, process.ExitCode);
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
