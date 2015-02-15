using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace ILRepacking
{
    class RepackAssemblies
    {
        private readonly RepackOptions options;
        private readonly ILogger logger;
        private readonly IFile file;

        internal List<string> MergedAssemblyFiles { get; set; }
        internal string PrimaryAssemblyFile { get; set; }
        // contains all 'other' assemblies, but not the primary assembly
        internal List<AssemblyDefinition> OtherAssemblies { get; set; }
        // contains all assemblies, primary and 'other'
        internal List<AssemblyDefinition> MergedAssemblies { get; set; }
        internal AssemblyDefinition TargetAssemblyDefinition { get; set; }
        internal AssemblyDefinition PrimaryAssemblyDefinition { get; set; }

        public RepackAssemblies(RepackOptions options, ILogger logger, IFile file)
        {
            this.options = options;
            this.logger = logger;
            this.file = file;
        }

        public void ReadInputAssemblies()
        {
            MergedAssemblyFiles = options.InputAssemblies.SelectMany(ResolveFile).Distinct().ToList();
            OtherAssemblies = new List<AssemblyDefinition>();

            var primary = MergedAssemblyFiles.FirstOrDefault();
            foreach (var result in MergedAssemblyFiles.Select(assembly => ReadInputAssembly(assembly, primary == assembly)).AsParallel())
            {
                if (result.IsPrimary)
                {
                    PrimaryAssemblyDefinition = result.Definition;
                    PrimaryAssemblyFile = result.Assembly;
                }
                else
                    OtherAssemblies.Add(result.Definition);

                // prevent writing PDB if we haven't read any
                options.DebugInfo &= result.SymbolsRead;
            }

            MergedAssemblies = new List<AssemblyDefinition>(OtherAssemblies);
            MergedAssemblies.Add(PrimaryAssemblyDefinition);
        }

        private AssemblyDefinitionContainer ReadInputAssembly(string assembly, bool isPrimary)
        {
            logger.INFO("Adding assembly for merge: " + assembly);
            try
            {
                ReaderParameters rp = new ReaderParameters(ReadingMode.Immediate) {AssemblyResolver = options.GlobalAssemblyResolver};
                // read PDB/MDB?
                if (options.DebugInfo && (file.Exists(Path.ChangeExtension(assembly, "pdb")) || file.Exists(assembly + ".mdb")))
                {
                    rp.ReadSymbols = true;
                }
                AssemblyDefinition mergeAsm;
                try
                {
                    mergeAsm = AssemblyDefinition.ReadAssembly(assembly, rp);
                }
                catch
                {
                    // cope with invalid symbol file
                    if (rp.ReadSymbols)
                    {
                        rp.ReadSymbols = false;
                        mergeAsm = AssemblyDefinition.ReadAssembly(assembly, rp);
                        logger.INFO("Failed to load debug information for " + assembly);
                    }
                    else
                    {
                        throw;
                    }
                }
                if (!options.AllowZeroPeKind && (mergeAsm.MainModule.Attributes & ModuleAttributes.ILOnly) == 0)
                    throw new ArgumentException("Failed to load assembly with Zero PeKind: " + assembly);

                return new AssemblyDefinitionContainer
                {
                    Assembly = assembly,
                    Definition = mergeAsm,
                    IsPrimary = isPrimary,
                    SymbolsRead = rp.ReadSymbols
                };
            }
            catch
            {
                logger.ERROR("Failed to load assembly " + assembly);
                throw;
            }
        }

        public class AssemblyDefinitionContainer
        {
            public bool SymbolsRead { get; set; }
            public AssemblyDefinition Definition { get; set; }
            public string Assembly { get; set; }
            public bool IsPrimary { get; set; }
        }

        private IEnumerable<string> ResolveFile(string s)
        {
            if (!options.AllowWildCards || s.IndexOfAny(new[] { '*', '?' }) == -1)
                return new[] { s };
            if (Path.GetDirectoryName(s).IndexOfAny(new[] { '*', '?' }) != -1)
                throw new Exception("Invalid path: " + s);
            string dir = Path.GetDirectoryName(s);
            if (String.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            return Directory.GetFiles(Path.GetFullPath(dir), Path.GetFileName(s));
        }

        public TargetRuntime GetTargetRuntime()
        {
            var runtime = PrimaryAssemblyDefinition.MainModule.Runtime;
            if (options.TargetPlatformVersion == null)
                return runtime;

            switch (options.TargetPlatformVersion)
            {
                case "v1":
                    runtime = TargetRuntime.Net_1_0;
                    break;
                case "v1.1":
                    runtime = TargetRuntime.Net_1_1;
                    break;
                case "v2":
                    runtime = TargetRuntime.Net_2_0;
                    break;
                case "v4":
                    runtime = TargetRuntime.Net_4_0;
                    break;
                default:
                    throw new ArgumentException("Invalid TargetPlatformVersion: \"" + options.TargetPlatformVersion + "\".");
            }
            return runtime;
        }
    }
}
