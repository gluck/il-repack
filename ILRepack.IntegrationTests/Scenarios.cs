using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

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

        [Test]
        public void GivenNetCore3WinFormsAppUsesImageResources_MergedCore3WinFormsApplicationRunsSuccessfully()
        {
            RunScenario("WindowsFormsTestNetCoreApp");
        }

        [Test]
        public void GivenNetCore3WpfAppUsesImageResources_MergedCore3WpfApplicationRunsSuccessfully()
        {
            RunScenario("WPFSampleApplicationCore");
        }

        [Test]
        public void GivenLibraryWithWpfPackUrisInClrStrings_MergedWpfApplicationRunsSuccessfully()
        {
            RunScenario("WPFPackUrisInClrStringsApplicationCore");
        }

        private void RunScenario(string scenarioName)
        {
            string scenarioExecutable = GetScenarioExecutable(scenarioName);

            AssertFileExists(scenarioExecutable);

            string fileName = scenarioExecutable;
            string arguments = null;

            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                arguments = fileName;
                fileName = "dotnet";
            }

            var processStartInfo = new ProcessStartInfo(fileName, arguments)
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
            string scenariosDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, @"..\..\..\Scenarios\");
            scenariosDirectory = Path.GetFullPath(scenariosDirectory);
            string scenarioDirectory = Path.Combine(scenariosDirectory, scenarioName);

            var directory = Path.Combine(
                scenarioDirectory,
                "bin",
                GetRunningConfiguration());
            directory = Path.GetFullPath(directory);
            var targetFrameworks = Directory
                .GetDirectories(directory)
                .Where(d => Directory.Exists(Path.Combine(d, "merged")));
            directory = targetFrameworks.FirstOrDefault();
            directory = Path.Combine(directory, "merged");
            var filePath = Path.Combine(directory, scenarioName + ".exe");
            if (!File.Exists(filePath))
            {
                filePath = Path.Combine(directory, scenarioName + ".dll");
            }

            return filePath;
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
