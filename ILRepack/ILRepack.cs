//
// Copyright (c) 2011 Francois Valdy
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
//
using ILRepacking.Steps;
using Mono.Cecil;
using Mono.Cecil.PE;
using Mono.Collections.Generic;
using Mono.Unix.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CustomAttributeNamedArgument = Mono.Cecil.CustomAttributeNamedArgument;

namespace ILRepacking
{
    public class ILRepack : IRepackContext
    {
        internal RepackOptions Options;
        internal ILogger Logger;

        internal List<string> MergedAssemblyFiles { get; set; }
        internal string PrimaryAssemblyFile { get; set; }
        // contains all 'other' assemblies, but not the primary assembly
        public List<AssemblyDefinition> OtherAssemblies { get; private set; }
        // contains all assemblies, primary and 'other'
        public List<AssemblyDefinition> MergedAssemblies { get; private set; }
        public AssemblyDefinition TargetAssemblyDefinition { get; private set; }
        public AssemblyDefinition PrimaryAssemblyDefinition { get; private set; }
        public RepackAssemblyResolver GlobalAssemblyResolver { get; } = new RepackAssemblyResolver();

        public ModuleDefinition TargetAssemblyMainModule => TargetAssemblyDefinition.MainModule;
        public ModuleDefinition PrimaryAssemblyMainModule => PrimaryAssemblyDefinition.MainModule;

        private IKVMLineIndexer _lineIndexer;
        private ReflectionHelper _reflectionHelper;
        private PlatformFixer _platformFixer;
        private MappingHandler _mappingHandler;

        private static readonly Regex TYPE_RE = new Regex("^(.*?), ([^>,]+), .*$");

        IKVMLineIndexer IRepackContext.LineIndexer => _lineIndexer;
        ReflectionHelper IRepackContext.ReflectionHelper => _reflectionHelper;
        PlatformFixer IRepackContext.PlatformFixer => _platformFixer;
        MappingHandler IRepackContext.MappingHandler => _mappingHandler;
        private readonly Dictionary<AssemblyDefinition, int> aspOffsets = new Dictionary<AssemblyDefinition, int>();

        private readonly RepackImporter _repackImporter;

        public ILRepack(RepackOptions options)
            : this(options, new RepackLogger())
        {
        }

        public ILRepack(RepackOptions options, ILogger logger)
        {
            Options = options;
            Logger = logger;
            _repackImporter = new RepackImporter(Logger, Options, this, aspOffsets);
        }

        private void ReadInputAssemblies()
        {
            MergedAssemblyFiles = Options.InputAssemblies.SelectMany(ResolveFile).Distinct().ToList();
            OtherAssemblies = new List<AssemblyDefinition>();
            // TODO: this could be parallelized to gain speed
            var primary = MergedAssemblyFiles.FirstOrDefault();
            foreach (string assembly in MergedAssemblyFiles)
            {
                var result = ReadInputAssembly(assembly, primary == assembly);
                if (result.IsPrimary)
                {
                    PrimaryAssemblyDefinition = result.Definition;
                    PrimaryAssemblyFile = result.Assembly;
                }
                else
                    OtherAssemblies.Add(result.Definition);

                // prevent writing PDB if we haven't read any
                Options.DebugInfo |= result.SymbolsRead;
            }

            MergedAssemblies = new List<AssemblyDefinition>(OtherAssemblies);
            MergedAssemblies.Add(PrimaryAssemblyDefinition);
        }

        private AssemblyDefinitionContainer ReadInputAssembly(string assembly, bool isPrimary)
        {
            Logger.Info("Adding assembly for merge: " + assembly);
            try
            {
                ReaderParameters rp = new ReaderParameters(ReadingMode.Immediate) { AssemblyResolver = GlobalAssemblyResolver };
                // read PDB/MDB?
                if (Options.DebugInfo && (File.Exists(Path.ChangeExtension(assembly, "pdb")) || File.Exists(assembly + ".mdb")))
                {
                    rp.ReadSymbols = true;
                }
                AssemblyDefinition mergeAsm;
                try
                {
                    mergeAsm = AssemblyDefinition.ReadAssembly(assembly, rp);
                }
                catch (BadImageFormatException e) when (!rp.ReadSymbols)
                {
                    throw new InvalidOperationException(
                        "ILRepack does not support merging non-.NET libraries (e.g.: native libraries)", e);
                }
                // cope with invalid symbol file
                catch (Exception) when (rp.ReadSymbols)
                {
                    rp.ReadSymbols = false;
                    try
                    {
                        mergeAsm = AssemblyDefinition.ReadAssembly(assembly, rp);
                    }
                    catch (BadImageFormatException e)
                    {
                        throw new InvalidOperationException(
                            "ILRepack does not support merging non-.NET libraries (e.g.: native libraries)", e);
                    }
                    Logger.Info("Failed to load debug information for " + assembly);
                }

                if (!Options.AllowZeroPeKind && (mergeAsm.MainModule.Attributes & ModuleAttributes.ILOnly) == 0)
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
                Logger.Error("Failed to load assembly " + assembly);
                throw;
            }
        }

        internal class AssemblyDefinitionContainer
        {
            public bool SymbolsRead { get; set; }
            public AssemblyDefinition Definition { get; set; }
            public string Assembly { get; set; }
            public bool IsPrimary { get; set; }
        }

        private IEnumerable<string> ResolveFile(string s)
        {
            if (!Options.AllowWildCards || s.IndexOfAny(new[] { '*', '?' }) == -1)
                return new[] { s };
            if (Path.GetDirectoryName(s).IndexOfAny(new[] { '*', '?' }) != -1)
                throw new Exception("Invalid path: " + s);
            string dir = Path.GetDirectoryName(s);
            if (String.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            return Directory.GetFiles(Path.GetFullPath(dir), Path.GetFileName(s));
        }

        public enum Kind
        {
            Dll,
            Exe,
            WinExe,
            SameAsPrimaryAssembly
        }


        private TargetRuntime ParseTargetPlatform()
        {
            TargetRuntime runtime = PrimaryAssemblyMainModule.Runtime;
            if (Options.TargetPlatformVersion != null)
            {
                switch (Options.TargetPlatformVersion)
                {
                    case "v2": runtime = TargetRuntime.Net_2_0; break;
                    case "v4": runtime = TargetRuntime.Net_4_0; break;
                    default: throw new ArgumentException($"Invalid TargetPlatformVersion: '{Options.TargetPlatformVersion}'");
                }
                _platformFixer.ParseTargetPlatformDirectory(runtime, Options.TargetPlatformDirectory);
            }
            return runtime;
        }

        private string ResolveTargetPlatformDirectory(string version)
        {
            if (version == null)
                return null;
            var platformBasePath = Path.GetDirectoryName(Path.GetDirectoryName(typeof(string).Assembly.Location));
            List<string> platformDirectories = new List<string>(Directory.GetDirectories(platformBasePath));
            var platformDir = version.Substring(1);
            if (platformDir.Length == 1) platformDir = platformDir + ".0";
            // mono platform dir is '2.0' while windows is 'v2.0.50727'
            var targetPlatformDirectory = platformDirectories
                .FirstOrDefault(x => Path.GetFileName(x).StartsWith(platformDir) || Path.GetFileName(x).StartsWith($"v{platformDir}"));
            if (targetPlatformDirectory == null)
                throw new ArgumentException($"Failed to find target platform '{Options.TargetPlatformVersion}' in '{platformBasePath}'");
            Logger.Info($"Target platform directory resolved to {targetPlatformDirectory}");
            return targetPlatformDirectory;
        }

        /// <summary>
        /// The actual repacking process, called by main after parsing arguments.
        /// When referencing this assembly, call this after setting the merge properties.
        /// </summary>
        public void Repack()
        {
            Options.Validate();
            _reflectionHelper = new ReflectionHelper(this);
            ResolveSearchDirectories();

            // Read input assemblies only after all properties are set.
            ReadInputAssemblies();
            GlobalAssemblyResolver.RegisterAssemblies(MergedAssemblies);

            _platformFixer = new PlatformFixer(PrimaryAssemblyMainModule.Runtime);
            _mappingHandler = new MappingHandler();
            bool hadStrongName = PrimaryAssemblyDefinition.Name.HasPublicKey;

            ModuleKind kind = PrimaryAssemblyMainModule.Kind;
            if (Options.TargetKind.HasValue)
            {
                switch (Options.TargetKind.Value)
                {
                    case Kind.Dll: kind = ModuleKind.Dll; break;
                    case Kind.Exe: kind = ModuleKind.Console; break;
                    case Kind.WinExe: kind = ModuleKind.Windows; break;
                }
            }
            TargetRuntime runtime = ParseTargetPlatform();

            // change assembly's name to correspond to the file we create
            string mainModuleName = Path.GetFileNameWithoutExtension(Options.OutputFile);

            if (TargetAssemblyDefinition == null)
            {
                AssemblyNameDefinition asmName = Clone(PrimaryAssemblyDefinition.Name);
                asmName.Name = mainModuleName;
                TargetAssemblyDefinition = AssemblyDefinition.CreateAssembly(asmName, mainModuleName,
                    new ModuleParameters()
                    {
                        Kind = kind,
                        Architecture = PrimaryAssemblyMainModule.Architecture,
                        AssemblyResolver = GlobalAssemblyResolver,
                        Runtime = runtime
                    });
            }
            else
            {
                // TODO: does this work or is there more to do?
                TargetAssemblyMainModule.Kind = kind;
                TargetAssemblyMainModule.Runtime = runtime;

                TargetAssemblyDefinition.Name.Name = mainModuleName;
                TargetAssemblyMainModule.Name = mainModuleName;
            }
            // set the main module attributes
            TargetAssemblyMainModule.Attributes = PrimaryAssemblyMainModule.Attributes;
            TargetAssemblyMainModule.Win32ResourceDirectory = MergeWin32Resources(PrimaryAssemblyMainModule.Win32ResourceDirectory, OtherAssemblies.Select(x => x.MainModule).Select(x => x.Win32ResourceDirectory));

            if (Options.Version != null)
                TargetAssemblyDefinition.Name.Version = Options.Version;
            // TODO: Win32 version/icon properties seem not to be copied... limitation in cecil 0.9x?
            StrongNameKeyPair snkp = null;
            if (Options.KeyFile != null && File.Exists(Options.KeyFile))
            {
                using (var stream = new FileStream(Options.KeyFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    snkp = new StrongNameKeyPair(stream);
                }
                TargetAssemblyDefinition.Name.PublicKey = snkp.PublicKey;
                TargetAssemblyDefinition.Name.Attributes |= AssemblyAttributes.PublicKey;
                TargetAssemblyMainModule.Attributes |= ModuleAttributes.StrongNameSigned;
            }
            else
            {
                TargetAssemblyDefinition.Name.PublicKey = null;
                TargetAssemblyMainModule.Attributes &= ~ModuleAttributes.StrongNameSigned;
            }
            _lineIndexer = new IKVMLineIndexer(this);

            List<IRepackStep> repackSteps = new List<IRepackStep>
            {
                new ReferencesRepackStep(Logger, this),
                new TypesRepackStep(Logger, this, _repackImporter, Options),
                new ResourcesRepackStep(Logger, this, Options),
                new AttributesRepackStep(Logger, this, _repackImporter, Options),
                new ReferencesFixStep(Logger, this, _repackImporter, Options),
                new XamlResourcePathPatcherStep(Logger, this)
            };

            foreach (var step in repackSteps)
            {
                step.Perform();
            }

            var parameters = new WriterParameters();
            if ((snkp != null) && !Options.DelaySign)
                parameters.StrongNameKeyPair = snkp;
            // write PDB/MDB?
            if (Options.DebugInfo)
                parameters.WriteSymbols = true;
            // create output directory if it does not exist
            var outputDir = Path.GetDirectoryName(Options.OutputFile);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Logger.Info("Output directory does not exist. Creating output directory: " + outputDir);
                Directory.CreateDirectory(outputDir);
            }
            TargetAssemblyDefinition.Write(Options.OutputFile, parameters);
            Logger.Info("Writing output assembly to disk");
            // If this is an executable and we are on linux/osx we should copy file permissions from
            // the primary assembly
            if (Environment.OSVersion.Platform == PlatformID.MacOSX || Environment.OSVersion.Platform == PlatformID.Unix)
            {
                Stat stat;
                Logger.Info("Copying permissions from " + PrimaryAssemblyFile);
                Syscall.stat(PrimaryAssemblyFile, out stat);
                Syscall.chmod(Options.OutputFile, stat.st_mode);
            }
            if (hadStrongName && !TargetAssemblyDefinition.Name.HasPublicKey)
                Options.StrongNameLost = true;

            // nice to have, merge .config (assembly configuration file) & .xml (assembly documentation)
            ConfigMerger.Process(this);
            if (Options.XmlDocumentation)
                DocumentationMerger.Process(this);

            // TODO: we're done here, the code below is only test code which can be removed once it's all running fine
            // 'verify' generated assembly
            AssemblyDefinition asm2 = AssemblyDefinition.ReadAssembly(Options.OutputFile, new ReaderParameters(ReadingMode.Immediate) { AssemblyResolver = GlobalAssemblyResolver });
            // lazy match on the name (not full) to catch requirements about merging different versions
            bool failed = false;
            foreach (var a in asm2.MainModule.AssemblyReferences.Where(x => MergedAssemblies.Any(y => Options.KeepOtherVersionReferences ? x.FullName == y.FullName : x.Name == y.Name.Name)))
            {
                // failed
                Logger.Error("Merged assembly still references " + a.FullName);
                failed = true;
            }
            if (failed)
                throw new Exception("Merging failed, see above errors");
        }

        private void ResolveSearchDirectories()
        {
            foreach (var dir in Options.SearchDirectories)
                GlobalAssemblyResolver.AddSearchDirectory(dir);
            var targetPlatformDirectory = Options.TargetPlatformDirectory ?? ResolveTargetPlatformDirectory(Options.TargetPlatformVersion);
            if (targetPlatformDirectory != null)
            {
                GlobalAssemblyResolver.AddSearchDirectory(targetPlatformDirectory);
                var facadesDirectory = Path.Combine(targetPlatformDirectory, "Facades");
                if (Directory.Exists(facadesDirectory))
                    GlobalAssemblyResolver.AddSearchDirectory(facadesDirectory);
            }
        }

        private ResourceDirectory MergeWin32Resources(ResourceDirectory primary, IEnumerable<ResourceDirectory> resources)
        {
            if (primary == null)
                return null;
            foreach (var ass in OtherAssemblies)
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
                if (isAspRes(parents, exist))
                {
                    aspOffsets[ass] = exist.Data.Length;
                    byte[] newData = new byte[exist.Data.Length + entry.Data.Length];
                    Array.Copy(exist.Data, 0, newData, 0, exist.Data.Length);
                    Array.Copy(entry.Data, 0, newData, exist.Data.Length, entry.Data.Length);
                    exist.Data = newData;
                }
                else if (!isVersionInfoRes(parents, exist))
                {
                    Logger.Warn(string.Format("Duplicate Win32 resource with id={0}, parents=[{1}], name={2} in assembly {3}, ignoring", entry.Id, string.Join(",", parents.Select(p => p.Name ?? p.Id.ToString()).ToArray()), entry.Name, ass.Name));
                }
                return;
            }
            if (exist.Data != null || entry.Data != null)
            {
                Logger.Warn("Inconsistent Win32 resources, ignoring");
                return;
            }
            parents.Add(exist);
            MergeDirectory(parents, exist.Directory, ass, entry.Directory);
            parents.RemoveAt(parents.Count - 1);
        }

        private static bool isAspRes(List<ResourceEntry> parents, ResourceEntry exist)
        {
            return exist.Id == 101 && parents.Count == 1 && parents[0].Id == 3771;
        }

        private static bool isVersionInfoRes(List<ResourceEntry> parents, ResourceEntry exist)
        {
            return exist.Id == 0 && parents.Count == 2 && parents[0].Id == 16 && parents[1].Id == 1;
        }

        public string FixStr(string content)
        {
            return FixStr(content, false);
        }

        public string FixReferenceInIkvmAttribute(string content)
        {
            return FixStr(content, true);
        }

        private string FixStr(string content, bool javaAttribute)
        {
            if (String.IsNullOrEmpty(content) || content.Length > 512 || content.IndexOf(", ") == -1 || content.StartsWith("System."))
                return content;
            // TODO fix "TYPE, ASSEMBLYNAME, CULTURE" pattern
            // TODO fix "TYPE, ASSEMBLYNAME, VERSION, CULTURE, TOKEN" pattern
            var match = TYPE_RE.Match(content);
            if (match.Success)
            {
                string type = match.Groups[1].Value;
                string targetAssemblyName = TargetAssemblyDefinition.FullName;
                if (javaAttribute)
                    targetAssemblyName = targetAssemblyName.Replace('.', '/') + ";";

                if (MergedAssemblies.Any(x => x.Name.Name == match.Groups[2].Value))
                {
                    return type + ", " + targetAssemblyName;
                }
            }
            return content;
        }

        public string FixTypeName(string assemblyName, string typeName)
        {
            // TODO handle renames
            return typeName;
        }

        public string FixAssemblyName(string assemblyName)
        {
            if (MergedAssemblies.Any(x => x.FullName == assemblyName))
            {
                // TODO no public key token !
                return TargetAssemblyDefinition.FullName;
            }
            return assemblyName;
        }

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

        public TypeDefinition GetMergedTypeFromTypeRef(TypeReference reference)
        {
            return _mappingHandler.GetRemappedType(reference);
        }

        public TypeReference GetExportedTypeFromTypeRef(TypeReference type)
        {
            return _mappingHandler.GetExportedRemappedType(type) ?? type;
        }
    }
}
