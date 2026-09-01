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

        [Test]
        public void GivenLibraryDuplicateFieldNames_MergedApplicationRunsSuccessfully()
        {
            RunScenario("LibraryDuplicateFieldNames");
        }

        [TestCase("Sre8", "SystemResourcesExtensions.Sre8", "net472")]
        [TestCase("Sre8", "SystemResourcesExtensions.Sre8", "net10.0")]
        [TestCase("Sre10", "SystemResourcesExtensions.Sre10", "net472")]
        [TestCase("Sre10", "SystemResourcesExtensions.Sre10", "net10.0")]
        public void GivenPreserializedResources_MergedApplicationRunsWithoutSreDependencies(
            string resourceExtensionsVersion,
            string assemblyName,
            string targetFramework)
        {
            RunScenario(Path.Combine("SystemResourcesExtensions", resourceExtensionsVersion), assemblyName, targetFramework, assertSreDependenciesAreMerged: true);
        }

        [TestCase("Sre8", "SystemResourcesExtensions.Sre8")]
        [TestCase("Sre10", "SystemResourcesExtensions.Sre10")]
        public void GivenPreserializedResourcesLoadedFromPluginDirectory_MergedNet472ApplicationRunsSuccessfully(
            string resourceExtensionsVersion,
            string assemblyName)
        {
            string scenarioExecutable = GetScenarioExecutable(Path.Combine("SystemResourcesExtensions", resourceExtensionsVersion), assemblyName, "net472");
            string hostExecutable = Path.Combine(
                GetScenarioDirectory("SystemResourcesExtensions"),
                "PluginHost",
                "bin",
                GetRunningConfiguration(),
                "net472",
                "PluginHost.exe");

            AssertFileExists(scenarioExecutable);
            AssertFileExists(hostExecutable);
            RunProcess("SystemResourcesExtensions plug-in", hostExecutable, '"' + scenarioExecutable + '"');
        }

        private void RunScenario(string scenarioName, string assemblyName = null, string targetFramework = null, bool assertSreDependenciesAreMerged = false)
        {
            string scenarioExecutable = GetScenarioExecutable(scenarioName, assemblyName, targetFramework);

            AssertFileExists(scenarioExecutable);
            if (assertSreDependenciesAreMerged)
            {
                string outputDirectory = Path.GetDirectoryName(scenarioExecutable);
                var dependencyAssemblies = Directory.GetFiles(outputDirectory, "*.dll")
                    .Where(path => !string.Equals(path, scenarioExecutable, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                Assert.That(dependencyAssemblies, Is.Empty,
                    "The merged application must not require System.Resources.Extensions or any of its dependencies beside it.");
            }

            string fileName = scenarioExecutable;
            string arguments = null;

            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                arguments = fileName;
                fileName = "dotnet";
            }

            RunProcess(scenarioName, fileName, arguments);
        }

        private static void RunProcess(string processName, string fileName, string arguments)
        {
            var processStartInfo = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            Process process = Process.Start(processStartInfo);
            Assert.NotNull(process);

            bool processEnded = process.WaitForExit(ScenarioProcessWaitTimeInMs);
            Console.WriteLine("\nScenario '{0}' STDOUT: {1}", processName, process.StandardOutput.ReadToEnd());

            Assert.That(processEnded, Is.True, "Process has not ended.");
            Assert.That(process.ExitCode, Is.EqualTo(0), "Process exited with error");
        }

        private string GetScenarioExecutable(string scenarioName, string assemblyName, string targetFramework)
        {
            string scenarioDirectory = GetScenarioDirectory(scenarioName);

            var directory = Path.Combine(
                scenarioDirectory,
                "bin",
                GetRunningConfiguration());
            directory = Path.GetFullPath(directory);
            if (targetFramework == null)
            {
                var targetFrameworks = Directory
                    .GetDirectories(directory)
                    .Where(d => Directory.Exists(Path.Combine(d, "merged")));
                directory = targetFrameworks.FirstOrDefault();
            }
            else
            {
                directory = Path.Combine(directory, targetFramework);
            }
            directory = Path.Combine(directory, "merged");
            assemblyName = assemblyName ?? Path.GetFileName(scenarioName);
            var filePath = Path.Combine(directory, assemblyName + ".exe");
            if (!File.Exists(filePath))
            {
                filePath = Path.Combine(directory, assemblyName + ".dll");
            }

            return filePath;
        }

        private static string GetScenarioDirectory(string scenarioName)
        {
            string scenariosDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, @"..\..\..\Scenarios\");
            scenariosDirectory = Path.GetFullPath(scenariosDirectory);
            return Path.Combine(scenariosDirectory, scenarioName);
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
