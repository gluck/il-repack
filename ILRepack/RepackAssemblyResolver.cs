using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mono.Cecil;

namespace ILRepacking
{
    internal delegate void AssemblyResolvedDelegate(string assemblyName, string location);

    internal class RepackAssemblyResolver : BaseAssemblyResolver
    {
        private readonly Dictionary<string, AssemblyDefinition> cache = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> assemblyPathsByFullAssemblyName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly ReaderParameters readerParameters;
        private bool runtimeDirectoriesInitialized;
        private Version systemRuntimeVersion;
        private static readonly Version netcoreVersionBoundary = new Version(4, 0, 10, 0);

        public event AssemblyResolvedDelegate AssemblyResolved;

        private static readonly HashSet<string> ignoreRuntimeDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "aspnetcorev2_inprocess",
            "clrcompression",
            "clretwrc",
            "clrgc",
            "clrjit",
            "coreclr",
            "D3DCompiler_47_cor3",
            "dbgshim",
            "hostpolicy",
            "Microsoft.DiaSymreader.Native.amd64",
            "Microsoft.DiaSymreader.Native.arm64",
            "Microsoft.DiaSymreader.Native.x76",
            "mscordaccore",
            "mscordbi",
            "mscorrc",
            "mscorrc.debug",
            "msquic",
            "PenImc_cor3",
            "PresentationNative_cor3",
            "System.IO.Compression.Native",
            "ucrtbase",
            "vcruntime140_cor3",
            "wpfgfx_cor3",
        };

        private static readonly HashSet<string> frameworkNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "mscorlib",
            "Accessibility",
            "Microsoft.CSharp",
            "Microsoft.VisualBasic",
            "Microsoft.VisualC",
            "netstandard",
            "PresentationCore",
            "PresentationFramework",
            "ReachFramework",
            "System",
            "UIAutomationClient",
            "UIAutomationProvider",
            "UIAutomationTypes",
            "WindowsBase",
            "WindowsFormsIntegration"
        };

        public RepackAssemblyResolver()
        {
            this.ResolveFailure += RepackAssemblyResolver_ResolveFailure;
            readerParameters = new ReaderParameters()
            {
                AssemblyResolver = this
            };
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            string fullName = name.FullName;
            if (cache.TryGetValue(fullName, out var assembly))
            {
                return assembly;
            }

            assembly = TryResolve(name, parameters);
            cache[fullName] = assembly;

            AssemblyResolved?.Invoke(fullName, assembly.MainModule.FileName);

            return assembly;
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            var result = Resolve(name, readerParameters);
            return result;
        }

        private AssemblyDefinition RepackAssemblyResolver_ResolveFailure(object sender, AssemblyNameReference reference)
        {
            return TryResolveFromCoreFixVersion(reference);
        }

        private AssemblyDefinition TryResolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (name.Name == "System.Runtime" && name.Version.Major != 0 && systemRuntimeVersion is null)
            {
                systemRuntimeVersion = name.Version;
            }

            bool resolveFromCoreFirst = IsFrameworkName(name.Name) && name.Version > netcoreVersionBoundary;

            // see https://github.com/gluck/il-repack/issues/347
            if (name.Name.Equals("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase) && name.Version.Major <= 10)
            {
                resolveFromCoreFirst = false;
            }

            // heuristic: assembly more likely to be Core after that version.
            // Try to resolve from Core first to prevent the base resolver
            // from resolving Core assemblies from the GAC
            if (resolveFromCoreFirst)
            {
                var fromCore = TryResolveFromCoreFixVersion(name);
                if (fromCore != null)
                {
                    return fromCore;
                }
            }

            var result = base.Resolve(name, parameters);

            return result;
        }

        private AssemblyDefinition TryResolveFromCoreFixVersion(AssemblyNameReference reference)
        {
            var result = TryResolveFromCore(reference);

            // in .NET Core, System.Configuration.dll 4.0.0.0 references System.Configuration.ConfigurationManager.dll 0.0.0.0
            // so we fish out the version of the actual runtime and try resolve that instead
            if (result == null)
            {
                var version = reference.Version;
                if (version.Major == 0 &&
                    version.Minor == 0 &&
                    version.Build == 0 &&
                    version.Revision == 0 &&
                    systemRuntimeVersion is not null)
                {
                    var referenceWithVersion = new AssemblyNameReference(reference.Name, systemRuntimeVersion);
                    referenceWithVersion.Culture = reference.Culture;
                    referenceWithVersion.PublicKeyToken = reference.PublicKeyToken;
                    result = TryResolveFromCore(referenceWithVersion);
                }
            }

            return result;
        }

        private AssemblyDefinition TryResolveFromCore(AssemblyNameReference reference)
        {
            InitializeDotnetRuntimeDirectories();

            string fullName = reference.FullName;
            if (assemblyPathsByFullAssemblyName.TryGetValue(fullName, out var filePath))
            {
                var result = ModuleDefinition.ReadModule(filePath, readerParameters).Assembly;
                return result;
            }

            return null;
        }

        public void RegisterAssembly(AssemblyDefinition assembly)
        {
            var name = assembly.Name.FullName;
            if (cache.ContainsKey(name))
            {
                return;
            }

            cache[name] = assembly;
        }

        private static bool IsFrameworkName(string shortName)
        {
            return
                shortName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                frameworkNames.Contains(shortName);
        }

        public void InitializeDotnetRuntimeDirectories()
        {
            if (runtimeDirectoriesInitialized)
            {
                return;
            }

            runtimeDirectoriesInitialized = true;

            try
            {
                var process = ProcessRunner.Run("dotnet", "--list-runtimes");
                if (process == null || process.ExitCode != 0)
                {
                    throw new Exception(".NET Core SDK list query failed with code " + process.ExitCode);
                }

                var allRuntimes = new List<string>();

                var reader = new StringReader(process.Output);

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var pathStart = line.LastIndexOf('[') + 1;
                    if (pathStart == 0)
                    {
                        continue;
                    }

                    var pathEnd = line.LastIndexOf(']');
                    if (pathEnd == -1 || pathEnd <= pathStart)
                    {
                        continue;
                    }

                    var path = line.Substring(pathStart, pathEnd - pathStart);
                    var runtimeInfo = line.Substring(0, pathStart - 1);
                    var parts = runtimeInfo.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        continue;
                    }

                    var fullPath = Path.Combine(path, parts[1]);
                    allRuntimes.Add(fullPath);
                }

                allRuntimes.Reverse();

                ReadRuntimes(allRuntimes);
            }
            catch (Exception ex)
            {
                if (ex is AggregateException aggregate)
                {
                    ex = aggregate.InnerException;
                }

                Application.Error($"Error when calling 'dotnet --list-runtimes': {ex.Message}");
            }
        }

        private void ReadRuntimes(IEnumerable<string> allRuntimes)
        {
            foreach (var directory in allRuntimes)
            {
                ReadRuntime(directory);
            }
        }

        private void ReadRuntime(string directory)
        {
            var files = Directory.GetFiles(directory, "*.dll");

            foreach (var filePath in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                if (ignoreRuntimeDlls.Contains(fileName) ||
                    fileName.StartsWith("api-ms-", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("mscordaccore_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var assemblyName = AssemblyName.GetAssemblyName(filePath);
                    string fullName = assemblyName.FullName;
                    if (!assemblyPathsByFullAssemblyName.ContainsKey(fullName))
                    {
                        assemblyPathsByFullAssemblyName[fullName] = filePath;
                    }
                }
                catch
                {
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            foreach (var assembly in cache.Values)
            {
                assembly.Dispose();
            }

            cache.Clear();

            base.Dispose(disposing);
        }
    }
}

