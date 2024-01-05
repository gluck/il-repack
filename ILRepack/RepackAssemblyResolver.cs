using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Mono.Cecil;

namespace ILRepacking
{
    public class RepackAssemblyResolver : DefaultAssemblyResolver
    {
        private bool runtimeDirectoriesInitialized;
        private readonly Dictionary<string, string> assemblyPathsByFullAssemblyName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        public RepackAssemblyResolver()
        {
            this.ResolveFailure += RepackAssemblyResolver_ResolveFailure;
        }

        private AssemblyDefinition RepackAssemblyResolver_ResolveFailure(object sender, AssemblyNameReference reference)
        {
            InitializeDotnetRuntimeDirectories();

            var result = TryResolve(reference);

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
                    result = TryResolve(referenceWithVersion);
                }
            }

            return result;
        }

        private AssemblyDefinition TryResolve(AssemblyNameReference reference)
        {
            string fullName = reference.FullName;
            if (assemblyPathsByFullAssemblyName.TryGetValue(fullName, out var filePath))
            {
                var result = ModuleDefinition.ReadModule(filePath).Assembly;
                return result;
            }

            return null;
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            return base.Resolve(name, parameters);
        }

        private Version systemRuntimeVersion;

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            var result = base.Resolve(name);

            if (name.Name == "System.Runtime" && name.Version.Major != 0 && systemRuntimeVersion is null)
            {
                systemRuntimeVersion = name.Version;
            }

            return result;
        }

        public new void RegisterAssembly(AssemblyDefinition assembly)
        {
            base.RegisterAssembly(assembly);
        }

        private void InitializeDotnetRuntimeDirectories()
        {
            if (runtimeDirectoriesInitialized)
            {
                return;
            }

            runtimeDirectoriesInitialized = true;

            var process = ProcessRunner.Run("dotnet", "--list-runtimes");
            if (process.ExitCode != 0)
            {
                throw new Exception(".NET Core SDK list query failed with code " + process.ExitCode);
            }

            var allRuntimes = new List<string>();

            var reader = new StringReader(process.Output);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var pathStart = line.LastIndexOf('[') + 1;
                var path = line.Substring(pathStart, line.LastIndexOf(']') - pathStart);
                var runtimeInfo = line.Substring(0, pathStart - 1);
                var parts = runtimeInfo.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var fullPath = Path.Combine(path, parts[1]);
                allRuntimes.Add(fullPath);
            }

            allRuntimes.Reverse();

            ReadRuntimes(allRuntimes);
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
    }
}
