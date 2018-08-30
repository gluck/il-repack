using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;

namespace ILRepack.IntegrationTests
{
    [TestFixture]
    [Platform(Include = "win")]
    public class Scenarios
    {
        private const int ScenarioProcessWaitTimeInMs = 10000;

        [Test]
        public void GivenXAMLThatUsesLibraryClass_MergedWPFApplicationRunsSuccessfully()
        {
            RunScenario("LibraryClassUsageInXAML");
        }

        [Test]
        public void GivenXAMLThatUsesLibraryUserControl_MergedWPFApplicationRunsSuccessfully()
        {
            RunScenario("LibraryUserControlUsageInXAML");
        }

        [Test]
        public void GivenXAMLThatUsesNestedLibraryUserControlAndClass_MergedWPFApplicationRunsSuccessfully()
        {
            RunScenario("NestedLibraryUsageInXAML");
        }

        [Test]
        public void GivenApplicationThatUsesThemingAndStylesFromA_MergedWPFApplicationRunsSuccessfully()
        {
            RunScenario("WPFThemingAndLibraryStyles");
        }

        [Test]
        public void GivenSampleApplicationWithMahAppsAndSystemWindowsInteractivityWPF_MergedWPFApplicationRunsSuccessfully()
        {
            RunScenario("WPFSampleApplication");
        }

        [Test]
        public void GivenDotNet462AppReferencingMicrosoftBclAsyncAndSystemRuntime_MergedApplicationRunsSuccessfully()
        {
            RunScenario("DotNet462Application");
        }

        [Test]
        public void GivenDotNet462AppUsingNetStandard2LibrarySetAndReflection_MergedApplicationRunsSuccessfully()
        {
            RunScenario("DotNet462NetStandard2");
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
            Console.WriteLine("\nScenario '{0}' STDOUT: {1}", scenarioName, process.StandardOutput.ReadToEnd());

            Assert.That(processEnded, Is.True, "Process has not ended.");
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
