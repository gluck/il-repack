using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.PE;

namespace ILRepacking
{
    class RepackAssemblies
    {
        private readonly RepackOptions options;
        private readonly ILogger logger;
        private readonly IFile file;

        internal List<string> MergedAssemblyFileNames { get; set; }
        internal List<AssemblyDefinition> MergedAssemblies { get; set; }

        internal AssemblyDefinition TargetAssemblyDefinition { get; set; }
        internal AssemblyDefinition PrimaryAssemblyDefinition { get; set; }
        internal string PrimaryAssemblyFileName { get; set; }

        internal Dictionary<AssemblyDefinition, int> AspOffsets { get; set; }

        public RepackAssemblies(RepackOptions options, ILogger logger, IFile file)
        {
            this.options = options;
            this.logger = logger;
            this.file = file;

            AspOffsets = new Dictionary<AssemblyDefinition, int>();
            MergedAssemblies = new List<AssemblyDefinition>();
        }

        internal IEnumerable<AssemblyDefinition> MergedAssembliesExceptPrimary
        {
            get
            {
                return MergedAssemblies.Where(assembly => assembly.Name.Name != Path.GetFileNameWithoutExtension(PrimaryAssemblyFileName));
            }
        }

        public void ReadInputAssemblies()
        {
            MergedAssemblyFileNames = options.InputAssemblies.SelectMany(ResolveFile).Distinct().ToList();

            PrimaryAssemblyFileName = MergedAssemblyFileNames.FirstOrDefault();
            foreach (var assemblyDefinition in MergedAssemblyFileNames.AsParallel().Select(ReadInputAssembly))
            {
                if (assemblyDefinition.Name.Name == Path.GetFileNameWithoutExtension(PrimaryAssemblyFileName))
                {
                    PrimaryAssemblyDefinition = assemblyDefinition;
                }
                MergedAssemblies.Add(assemblyDefinition);
            }
        }

        private AssemblyDefinition ReadInputAssembly(string fileName)
        {
            logger.INFO("Adding fileName for merge: " + fileName);
            var readerParameters = new ReaderParameters(ReadingMode.Immediate) { AssemblyResolver = options.GlobalAssemblyResolver };

            var debugFileExists = file.Exists(Path.ChangeExtension(fileName, "pdb")) || file.Exists(fileName + ".mdb");
            options.DebugInfo &= debugFileExists;
            readerParameters.ReadSymbols = options.DebugInfo;
            if (readerParameters.ReadSymbols)
                logger.INFO("Adding pdb for merge.");
               
            AssemblyDefinition assemblyDefinition;
            try
            {
                assemblyDefinition = AssemblyDefinition.ReadAssembly(fileName, readerParameters);
            }
            catch
            {
                // cope with invalid symbol file
                if (readerParameters.ReadSymbols)
                {
                    options.DebugInfo = false;
                    readerParameters.ReadSymbols = false;
                    assemblyDefinition = AssemblyDefinition.ReadAssembly(fileName, readerParameters);
                    logger.INFO("Failed to load debug information for " + fileName);
                }
                else
                {
                    logger.ERROR("Failed to load assembly" + fileName);
                    throw;
                }
            }
            if (!options.AllowZeroPeKind && (assemblyDefinition.MainModule.Attributes & ModuleAttributes.ILOnly) == 0)
            {
                logger.ERROR("Failed to load assembly" + fileName);
                throw new ArgumentException("Failed to load assembly with Zero PeKind: " + fileName);
            }

            return assemblyDefinition;
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

        public ModuleKind GetTargetModuleKind()
        {
            var kind = PrimaryAssemblyDefinition.MainModule.Kind;
            if (!options.TargetKind.HasValue) 
                return kind;
            switch (options.TargetKind.Value)
            {
                case ILRepack.Kind.Dll: kind = ModuleKind.Dll; break;
                case ILRepack.Kind.Exe: kind = ModuleKind.Console; break;
                case ILRepack.Kind.WinExe: kind = ModuleKind.Windows; break;
            }
            return kind;
        }

        public void ParseTargetAssemblyDefinition()
        {
            var kind = GetTargetModuleKind();
            var runtime = GetTargetRuntime();
            // change assembly's name to correspond to the file we create
            string mainModuleName = Path.GetFileNameWithoutExtension(options.OutputFile);
            if (TargetAssemblyDefinition == null)
            {
                AssemblyNameDefinition asmName = Clone(PrimaryAssemblyDefinition.Name);
                asmName.Name = mainModuleName;
                TargetAssemblyDefinition = AssemblyDefinition.CreateAssembly(asmName, mainModuleName,
                    new ModuleParameters()
                        {
                            Kind = kind,
                            Architecture = PrimaryAssemblyDefinition.MainModule.Architecture,
                            AssemblyResolver = options.GlobalAssemblyResolver,
                            Runtime = runtime
                        });
            }
            else
            {
                // TODO: does this work or is there more to do?
                TargetAssemblyDefinition.MainModule.Kind = kind;
                TargetAssemblyDefinition.MainModule.Runtime = runtime;

                TargetAssemblyDefinition.Name.Name = mainModuleName;
                TargetAssemblyDefinition.MainModule.Name = mainModuleName;
            }
            // set the main module attributes
            TargetAssemblyDefinition.MainModule.Attributes = PrimaryAssemblyDefinition.MainModule.Attributes;
            TargetAssemblyDefinition.MainModule.Win32ResourceDirectory = MergeWin32Resources(PrimaryAssemblyDefinition.MainModule.Win32ResourceDirectory, MergedAssembliesExceptPrimary.Select(x => x.MainModule).Select(x => x.Win32ResourceDirectory));

            if (options.Version != null)
                TargetAssemblyDefinition.Name.Version = options.Version;
            // TODO: Win32 version/icon properties seem not to be copied... limitation in cecil 0.9x?
            StrongNameKeyPair snkp = null;
            if (options.KeyFile != null && File.Exists(options.KeyFile))
            {
                using (var stream = new FileStream(options.KeyFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    snkp = new StrongNameKeyPair(stream);
                }
                TargetAssemblyDefinition.Name.PublicKey = snkp.PublicKey;
                TargetAssemblyDefinition.Name.Attributes |= AssemblyAttributes.PublicKey;
                TargetAssemblyDefinition.MainModule.Attributes |= ModuleAttributes.StrongNameSigned;
            }
            else
            {
                TargetAssemblyDefinition.Name.PublicKey = null;
                TargetAssemblyDefinition.MainModule.Attributes &= ~ModuleAttributes.StrongNameSigned;
            }

            var parameters = new WriterParameters();
            if ((snkp != null) && !options.DelaySign)
                parameters.StrongNameKeyPair = snkp;
            // write PDB/MDB?
            if (options.DebugInfo)
                parameters.WriteSymbols = true;
            TargetAssemblyDefinition.Write(options.OutputFile, parameters);
        }

        // Real stuff below //
        // These methods are somehow a merge between the clone methods of Cecil 0.6 and the import ones of 0.9
        // They use Cecil's MetaDataImporter to rebase imported stuff into the new assembly, but then another pass is required
        //  to clean the TypeRefs Cecil keeps around (although the generated IL would be kind-o valid without, whatever 'valid' means)
        private AssemblyNameDefinition Clone(AssemblyNameDefinition assemblyName)
        {
            AssemblyNameDefinition asmName = new AssemblyNameDefinition(assemblyName.Name, assemblyName.Version);
            asmName.Attributes = assemblyName.Attributes;
            asmName.Culture = assemblyName.Culture;
            asmName.Hash = assemblyName.Hash;
            asmName.HashAlgorithm = assemblyName.HashAlgorithm;
            asmName.PublicKey = assemblyName.PublicKey;
            asmName.PublicKeyToken = assemblyName.PublicKeyToken;
            return asmName;
        }

        private ResourceDirectory MergeWin32Resources(ResourceDirectory primary, IEnumerable<ResourceDirectory> resources)
        {
            if (primary == null)
                return null;
            foreach (var ass in MergedAssembliesExceptPrimary)
            {
                MergeDirectory(new List<ResourceEntry>(), primary, ass, ass.MainModule.Win32ResourceDirectory);
            }
            return primary;
        }

        private void MergeDirectory(List<ResourceEntry> parents, ResourceDirectory ret, AssemblyDefinition ass, ResourceDirectory directory)
        {
            foreach (var entry in directory.Entries)
            {
                var exist = ret.Entries.FirstOrDefault(x => entry.Name == null ? entry.Id == x.Id : entry.Name == x.Name);
                if (exist == null)
                    ret.Entries.Add(entry);
                else
                    MergeEntry(parents, exist, ass, entry);
            }
        }

        private void MergeEntry(List<ResourceEntry> parents, ResourceEntry exist, AssemblyDefinition ass, ResourceEntry entry)
        {
            if (exist.Data != null && entry.Data != null)
            {
                if (IsAspRes(parents, exist))
                {
                    AspOffsets[ass] = exist.Data.Length;
                    byte[] newData = new byte[exist.Data.Length + entry.Data.Length];
                    Array.Copy(exist.Data, 0, newData, 0, exist.Data.Length);
                    Array.Copy(entry.Data, 0, newData, exist.Data.Length, entry.Data.Length);
                    exist.Data = newData;
                }
                else if (!IsVersionInfoRes(parents, exist))
                {
                    logger.WARN(string.Format("Duplicate Win32 resource with id={0}, parents=[{1}], name={2} in assembly {3}, ignoring", entry.Id, string.Join(",", parents.Select(p => p.Name ?? p.Id.ToString()).ToArray()), entry.Name, ass.Name));
                }
                return;
            }
            if (exist.Data != null || entry.Data != null)
            {
                logger.WARN("Inconsistent Win32 resources, ignoring");
                return;
            }
            parents.Add(exist);
            MergeDirectory(parents, exist.Directory, ass, entry.Directory);
            parents.RemoveAt(parents.Count - 1);
        }

        public bool TryGetAspOffset(AssemblyDefinition assemblyDefinition, out int offset)
        {
            return AspOffsets.TryGetValue(assemblyDefinition, out offset);
        }

        private static bool IsAspRes(List<ResourceEntry> parents, ResourceEntry exist)
        {
            return exist.Id == 101 && parents.Count == 1 && parents[0].Id == 3771;
        }

        private static bool IsVersionInfoRes(List<ResourceEntry> parents, ResourceEntry exist)
        {
            return exist.Id == 0 && parents.Count == 2 && parents[0].Id == 16 && parents[1].Id == 1;
        }
    }
}
