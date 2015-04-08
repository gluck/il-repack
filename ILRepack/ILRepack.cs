
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

using Mono.Cecil;
using Mono.Cecil.PE;
using Mono.Collections.Generic;
using Mono.Unix.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security;
using System.Text.RegularExpressions;
using CustomAttributeNamedArgument = Mono.Cecil.CustomAttributeNamedArgument;

namespace ILRepacking
{
    public class ILRepack : IRepackContext, IRepackCopier
    {
        internal RepackOptions Options;
        internal ILogger Logger;

        internal List<string> MergedAssemblyFiles { get; set; }
        internal string PrimaryAssemblyFile { get; set; }
        // contains all 'other' assemblies, but not the primary assembly
        internal List<AssemblyDefinition> OtherAssemblies { get; set; }
        // contains all assemblies, primary and 'other'
        public List<AssemblyDefinition> MergedAssemblies { get; private set; }
        internal AssemblyDefinition TargetAssemblyDefinition { get; set; }
        internal AssemblyDefinition PrimaryAssemblyDefinition { get; set; }
        public IKVMLineIndexer LineIndexer { get; private set; }

        // helpers
        public ModuleDefinition TargetAssemblyMainModule { get { return TargetAssemblyDefinition.MainModule; } }

        private ModuleDefinition PrimaryAssemblyMainModule { get { return PrimaryAssemblyDefinition.MainModule; } }

        public ReflectionHelper ReflectionHelper { get;private set; }
        private static readonly Regex TYPE_RE = new Regex("^(.*?), ([^>,]+), .*$");

        public PlatformFixer PlatformFixer { get; private set; }
        public MappingHandler MappingHandler { get; private set; }
        private readonly Dictionary<AssemblyDefinition, int> aspOffsets = new Dictionary<AssemblyDefinition, int>();

        private readonly IRepackImporter _repackImporter;

        internal ILRepack(RepackOptions options, ILogger logger)
        {
            Options = options;
            Logger = logger;
            _repackImporter = new RepackImporter(logger, Options, this, this, aspOffsets);
        }

        public void Merge()
        {
            Repack();
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
                Options.DebugInfo &= result.SymbolsRead;
            }

            MergedAssemblies = new List<AssemblyDefinition>(OtherAssemblies);
            MergedAssemblies.Add(PrimaryAssemblyDefinition);
        }

        private AssemblyDefinitionContainer ReadInputAssembly(string assembly, bool isPrimary)
        {
            Logger.INFO("Adding assembly for merge: " + assembly);
            try
            {
                ReaderParameters rp = new ReaderParameters(ReadingMode.Immediate) {AssemblyResolver = Options.GlobalAssemblyResolver};
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
                catch
                {
                    // cope with invalid symbol file
                    if (rp.ReadSymbols)
                    {
                        rp.ReadSymbols = false;
                        mergeAsm = AssemblyDefinition.ReadAssembly(assembly, rp);
                        Logger.INFO("Failed to load debug information for " + assembly);
                    }
                    else
                    {
                        throw;
                    }
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
                Logger.ERROR("Failed to load assembly " + assembly);
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


        protected TargetRuntime ParseTargetPlatform()
        {
            TargetRuntime runtime = PrimaryAssemblyMainModule.Runtime;
            if (Options.TargetPlatformVersion != null)
            {
                switch (Options.TargetPlatformVersion)
                {
                    case "v1": runtime = TargetRuntime.Net_1_0; break;
                    case "v1.1": runtime = TargetRuntime.Net_1_1; break;
                    case "v2": runtime = TargetRuntime.Net_2_0; break;
                    case "v4": runtime = TargetRuntime.Net_4_0; break;
                    default: throw new ArgumentException("Invalid TargetPlatformVersion: \"" + Options.TargetPlatformVersion + "\".");
                }
                PlatformFixer.ParseTargetPlatformDirectory(runtime, Options.TargetPlatformDirectory);
            }
            return runtime;
        }

        /// <summary>
        /// Check if a type's FullName matches a Reges to exclude it from internalizing.
        /// </summary>
        private bool ShouldInternalize(string typeFullName)
        {
            if (Options.ExcludeInternalizeMatches == null)
            {
                return Options.Internalize;
            }
            string withSquareBrackets = "[" + typeFullName + "]";
            foreach (Regex r in Options.ExcludeInternalizeMatches)
                if (r.IsMatch(typeFullName) || r.IsMatch(withSquareBrackets))
                    return false;
            return true;
        }

        /// <summary>
        /// The actual repacking process, called by main after parsing arguments.
        /// When referencing this assembly, call this after setting the merge properties.
        /// </summary>
        public void Repack()
        {
            ReflectionHelper = new ReflectionHelper(this);
            Options.ParseProperties();

            // Read input assemblies only after all properties are set.
            ReadInputAssemblies();
            Options.GlobalAssemblyResolver.RegisterAssemblies(MergedAssemblies);

            PlatformFixer = new PlatformFixer(PrimaryAssemblyMainModule.Runtime);
            MappingHandler = new MappingHandler();
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
                            AssemblyResolver = Options.GlobalAssemblyResolver,
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
            LineIndexer = new IKVMLineIndexer(this);

            RepackReferences();
            RepackTypes();
            RepackExportedTypes();
            RepackResources();
            RepackAttributes();

            var fixator = new ReferenceFixator(this);
            if (PrimaryAssemblyMainModule.EntryPoint != null)
            {
                TargetAssemblyMainModule.EntryPoint = fixator.Fix(_repackImporter.Import(PrimaryAssemblyDefinition.EntryPoint)).Resolve();
            }

            Logger.INFO("Fixing references");
            // this step travels through all TypeRefs & replaces them by matching TypeDefs

            foreach (var r in TargetAssemblyMainModule.Types)
            {
                fixator.FixReferences(r);
            }
            foreach (var r in TargetAssemblyMainModule.Types)
            {
                fixator.FixMethodVisibility(r);
            }
            fixator.FixReferences(TargetAssemblyDefinition.MainModule.ExportedTypes);
            fixator.FixReferences(TargetAssemblyDefinition.CustomAttributes);
            fixator.FixReferences(TargetAssemblyDefinition.SecurityDeclarations);
            fixator.FixReferences(TargetAssemblyMainModule.CustomAttributes);

            // final reference cleanup (Cecil Import automatically added them)
            foreach (AssemblyDefinition asm in MergedAssemblies)
            {
                foreach (var refer in TargetAssemblyMainModule.AssemblyReferences.ToArray())
                {
                    // remove all referenced assemblies with same same, as we didn't bother on the version when merging
                    // in case we reference same assemblies with different versions, there might be prior errors if we don't merge the 'largest one'
                    if (Options.KeepOtherVersionReferences ? refer.FullName == asm.FullName : refer.Name == asm.Name.Name)
                    {
                        TargetAssemblyMainModule.AssemblyReferences.Remove(refer);
                    }
                }
            }

            var parameters = new WriterParameters();
            if ((snkp != null) && !Options.DelaySign)
                parameters.StrongNameKeyPair = snkp;
            // write PDB/MDB?
            if (Options.DebugInfo)
                parameters.WriteSymbols = true;
            TargetAssemblyDefinition.Write(Options.OutputFile, parameters);
            Logger.INFO("Writing output assembly to disk");
            // If this is an executable and we are on linux/osx we should copy file permissions from
            // the primary assembly
            if (Environment.OSVersion.Platform == PlatformID.MacOSX || Environment.OSVersion.Platform == PlatformID.Unix)
            {
                Stat stat;
                Logger.INFO("Copying permissions from " + PrimaryAssemblyFile);
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
            AssemblyDefinition asm2 = AssemblyDefinition.ReadAssembly(Options.OutputFile, new ReaderParameters(ReadingMode.Immediate) { AssemblyResolver = Options.GlobalAssemblyResolver });
            // lazy match on the name (not full) to catch requirements about merging different versions
            bool failed = false;
            foreach (var a in asm2.MainModule.AssemblyReferences.Where(x => MergedAssemblies.Any(y => Options.KeepOtherVersionReferences ? x.FullName == y.FullName : x.Name == y.Name.Name)))
            {
                // failed
                Logger.ERROR("Merged assembly still references " + a.FullName);
                failed = true;
            }
            if (failed)
                throw new Exception("Merging failed, see above errors");
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
                    Logger.WARN(string.Format("Duplicate Win32 resource with id={0}, parents=[{1}], name={2} in assembly {3}, ignoring", entry.Id, string.Join(",", parents.Select(p => p.Name ?? p.Id.ToString()).ToArray()), entry.Name, ass.Name));
                }
                return;
            }
            if (exist.Data != null || entry.Data != null)
            {
                Logger.WARN("Inconsistent Win32 resources, ignoring");
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

        private void RepackAttributes()
        {
            if (Options.CopyAttributes)
            {
                CleanupAttributes(typeof(CompilationRelaxationsAttribute).FullName, x => x.ConstructorArguments.Count == 1 /* TODO && x.ConstructorArguments[0].Value.Equals(1) */);
                CleanupAttributes(typeof(SecurityTransparentAttribute).FullName, null);
                CleanupAttributes(typeof(SecurityCriticalAttribute).FullName, x => x.ConstructorArguments.Count == 0);
                CleanupAttributes(typeof(AllowPartiallyTrustedCallersAttribute).FullName, x => x.ConstructorArguments.Count == 0);
                CleanupAttributes("System.Security.SecurityRulesAttribute", x => x.ConstructorArguments.Count == 0);
                RemoveAttributes(typeof(InternalsVisibleToAttribute).FullName, ca =>
                                                                                {
                                                                                    String name = (string)ca.ConstructorArguments[0].Value;
                                                                                    int idx;
                                                                                    if ((idx = name.IndexOf(", PublicKey=")) != -1)
                                                                                    {
                                                                                        name = name.Substring(0, idx);
                                                                                    }
                                                                                    return MergedAssemblies.Any(x => x.Name.Name == name);
                                                                                });
                RemoveAttributes(typeof(AssemblyDelaySignAttribute).FullName, null);
                RemoveAttributes(typeof(AssemblyKeyFileAttribute).FullName, null);
                RemoveAttributes(typeof(AssemblyKeyNameAttribute).FullName, null);
                foreach (var ass in MergedAssemblies)
                {
                    CopyCustomAttributes(ass.CustomAttributes, TargetAssemblyDefinition.CustomAttributes, Options.AllowMultipleAssemblyLevelAttributes, null);
                }
                foreach (var mod in MergedAssemblies.SelectMany(x => x.Modules))
                {
                    CopyCustomAttributes(mod.CustomAttributes, TargetAssemblyMainModule.CustomAttributes, Options.AllowMultipleAssemblyLevelAttributes, null);
                }
            }
            else if (Options.AttributeFile != null)
            {
                AssemblyDefinition attributeAsm = AssemblyDefinition.ReadAssembly(Options.AttributeFile, new ReaderParameters(ReadingMode.Immediate) { AssemblyResolver = Options.GlobalAssemblyResolver });
                CopyCustomAttributes(attributeAsm.CustomAttributes, TargetAssemblyDefinition.CustomAttributes, null);
                CopyCustomAttributes(attributeAsm.CustomAttributes, TargetAssemblyMainModule.CustomAttributes, null);
                // TODO: should copy Win32 resources, too
            }
            else
            {
                CopyCustomAttributes(PrimaryAssemblyDefinition.CustomAttributes, TargetAssemblyDefinition.CustomAttributes, null);
                CopyCustomAttributes(PrimaryAssemblyMainModule.CustomAttributes, TargetAssemblyMainModule.CustomAttributes, null);
                // TODO: should copy Win32 resources, too
            }
            CopySecurityDeclarations(PrimaryAssemblyDefinition.SecurityDeclarations, TargetAssemblyDefinition.SecurityDeclarations, null);
        }

        private void RepackTypes()
        {
            Logger.INFO("Processing types");
            // merge types, this differs between 'primary' and 'other' assemblies regarding internalizing

            foreach (var r in PrimaryAssemblyDefinition.Modules.SelectMany(x => x.Types))
            {
                Logger.VERBOSE("- Importing " + r);
                _repackImporter.Import(r, TargetAssemblyMainModule.Types, false);
            }
            foreach (var m in OtherAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.Types)
                {
                    Logger.VERBOSE("- Importing " + r);
                    _repackImporter.Import(r, TargetAssemblyMainModule.Types, ShouldInternalize(r.FullName));
                }
            }
        }

        private void RepackExportedTypes()
        {
            Logger.INFO("Processing types");
            foreach (var m in MergedAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                {
                    MappingHandler.StoreExportedType(m, r.FullName, CreateReference(r));
                }
            }
            foreach (var r in PrimaryAssemblyDefinition.Modules.SelectMany(x => x.ExportedTypes))
            {
                Logger.VERBOSE("- Importing Exported Type" + r);
                _repackImporter.Import(r, TargetAssemblyMainModule.ExportedTypes, TargetAssemblyMainModule);
            }
            foreach (var m in OtherAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                {
                    if (!ShouldInternalize(r.FullName))
                    {
                        Logger.VERBOSE("- Importing Exported Type " + r);
                        _repackImporter.Import(r, TargetAssemblyMainModule.ExportedTypes, TargetAssemblyMainModule);
                    }
                    else
                    {
                        Logger.VERBOSE("- Skipping Exported Type " + r);
                    }
                }
            }
        }

        private void RepackReferences()
        {
            Logger.INFO("Processing references");
            // Add all AssemblyReferences to merged assembly (probably not necessary)
            foreach (var z in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.AssemblyReferences))
            {
                string name = z.Name;
                if (!MergedAssemblies.Any(y => y.Name.Name == name) && TargetAssemblyDefinition.Name.Name != name && !TargetAssemblyMainModule.AssemblyReferences.Any(y => y.Name == name && z.Version == y.Version))
                {
                    // TODO: fix .NET runtime references?
                    // - to target a specific runtime version or
                    // - to target a single version if merged assemblies target different versions
                    Logger.VERBOSE("- add reference " + z);
                    AssemblyNameReference fixedRef = PlatformFixer.FixPlatformVersion(z);
                    TargetAssemblyMainModule.AssemblyReferences.Add(fixedRef);
                }
            }
            LineIndexer.PostRepackReferences();

            // add all module references (pinvoke dlls)
            foreach (var z in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.ModuleReferences))
            {
                string name = z.Name;
                if (!TargetAssemblyMainModule.ModuleReferences.Any(y => y.Name == name))
                {
                    TargetAssemblyMainModule.ModuleReferences.Add(z);
                }
            }
        }

        private void RepackResources()
        {
            Logger.INFO("Processing resources");
            // merge resources
            List<string> repackList = null;
            EmbeddedResource repackListRes = null;
            Dictionary<string, List<int>> ikvmExportsLists = null;
            EmbeddedResource ikvmExports = null;
            if (!Options.NoRepackRes)
            {
                repackList = MergedAssemblies.Select(a => a.FullName).ToList();
                repackListRes = GetRepackListResource(repackList);
                TargetAssemblyMainModule.Resources.Add(repackListRes);
            }
            foreach (var r in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.Resources))
            {
                if (r.Name == "ILRepack.List")
                {
                    if (!Options.NoRepackRes && r is EmbeddedResource)
                    {
                        MergeRepackListResource(ref repackList, ref repackListRes, (EmbeddedResource)r);
                    }
                }
                else if (r.Name == "ikvm.exports")
                {
                    if (r is EmbeddedResource)
                    {
                        MergeIkvmExportsResource(ref ikvmExportsLists, ref ikvmExports, (EmbeddedResource)r);
                    }
                }
                else
                {
                    if (!Options.AllowDuplicateResources && TargetAssemblyMainModule.Resources.Any(x => x.Name == r.Name))
                    {
                        // Not much we can do about 'ikvm__META-INF!MANIFEST.MF'
                        Logger.WARN("Ignoring duplicate resource " + r.Name);
                    }
                    else
                    {
                        Logger.VERBOSE("- Importing " + r.Name);
                        var nr = r;
                        switch (r.ResourceType)
                        {
                            case ResourceType.AssemblyLinked:
                                // TODO
                                Logger.WARN("AssemblyLinkedResource reference may need to be fixed (to link to newly created assembly)" + r.Name);
                                break;
                            case ResourceType.Linked:
                                // TODO ? (or not)
                                break;
                            case ResourceType.Embedded:
                                var er = (EmbeddedResource)r;
                                if (er.Name.EndsWith(".resources"))
                                {
                                    nr = FixResxResource(er);
                                }
                                break;
                        }
                        TargetAssemblyMainModule.Resources.Add(nr);
                    }
                }
            }
        }

        private void MergeRepackListResource(ref List<string> repackList, ref EmbeddedResource repackListRes, EmbeddedResource r)
        {
            var others = (string[])new BinaryFormatter().Deserialize(r.GetResourceStream());
            repackList = repackList.Union(others).ToList();
            EmbeddedResource repackListRes2 = GetRepackListResource(repackList);
            TargetAssemblyMainModule.Resources.Remove(repackListRes);
            TargetAssemblyMainModule.Resources.Add(repackListRes2);
            repackListRes = repackListRes2;
        }

        private void MergeIkvmExportsResource(ref Dictionary<string, List<int>> lists, ref EmbeddedResource existing, EmbeddedResource extra)
        {
            if (existing == null)
            {
                lists = ExtractIkvmExportsLists(extra);
                TargetAssemblyMainModule.Resources.Add(existing = extra);
            }
            else
            {
                TargetAssemblyMainModule.Resources.Remove(existing);
                var lists2 = ExtractIkvmExportsLists(extra);
                foreach (KeyValuePair<string, List<int>> kv in lists2)
                {
                    List<int> v;
                    if (!lists.TryGetValue(kv.Key, out v))
                    {
                        lists.Add(kv.Key, kv.Value);
                    }
                    else if (v != null)
                    {
                        if (kv.Value == null) // wildcard export
                            lists[kv.Key] = null;
                        else
                            lists[kv.Key] = v.Union(kv.Value).ToList();
                    }
                }
                existing = GenerateIkvmExports(lists);
                TargetAssemblyMainModule.Resources.Add(existing);
            }
        }

        private static Dictionary<string, List<int>> ExtractIkvmExportsLists(EmbeddedResource extra)
        {
            Dictionary<string, List<int>> ikvmExportsLists = new Dictionary<string, List<int>>();
            BinaryReader rdr = new BinaryReader(extra.GetResourceStream());
            int assemblyCount = rdr.ReadInt32();
            for (int i = 0; i < assemblyCount; i++)
            {
                var str = rdr.ReadString();
                int typeCount = rdr.ReadInt32();
                if (typeCount == 0)
                {
                    ikvmExportsLists.Add(str, null);
                }
                else
                {
                    var types = new List<int>();
                    ikvmExportsLists.Add(str, types);
                    for (int j = 0; j < typeCount; j++)
                        types.Add(rdr.ReadInt32());
                }
            }
            return ikvmExportsLists;
        }

        private static EmbeddedResource GenerateIkvmExports(Dictionary<string, List<int>> lists)
        {
            using (var stream = new MemoryStream())
            {
                var bw = new BinaryWriter(stream);
                bw.Write(lists.Count);
                foreach (KeyValuePair<string, List<int>> kv in lists)
                {
                    bw.Write(kv.Key);
                    if (kv.Value == null)
                    {
                        // wildcard export
                        bw.Write(0);
                    }
                    else
                    {
                        bw.Write(kv.Value.Count);
                        foreach (int hash in kv.Value)
                        {
                            bw.Write(hash);
                        }
                    }
                }
                return new EmbeddedResource("ikvm.exports", ManifestResourceAttributes.Public, stream.ToArray());
            }
        }

        private Resource FixResxResource(EmbeddedResource er)
        {
            MemoryStream stream = (MemoryStream)er.GetResourceStream();
            var output = new MemoryStream((int)stream.Length);
            var rw = new ResourceWriter(output);
            using (var rr = new ResReader(stream))
            {
                foreach (var res in rr)
                {
                    if (res.type == "ResourceTypeCode.String" || res.type.StartsWith("System.String"))
                    {
                        string content = (string)rr.GetObject(res);
                        content = FixStr(content);
                        rw.AddResource(res.name, content);
                    }
                    else
                    {
                        string fix = FixStr(res.type);
                        if (fix == res.type)
                        {
                            rw.AddResourceData(res.name, res.type, res.data);
                        }
                        else
                        {
                            var output2 = new MemoryStream(res.data.Length);
                            var sr = new SerReader(this, new MemoryStream(res.data), output2);
                            sr.Stream();
                            rw.AddResourceData(res.name, fix, output2.ToArray());
                        }
                    }
                }
            }
            rw.Generate();
            output.Position = 0;
            return new EmbeddedResource(er.Name, er.Attributes, output);
        }

        internal string FixStr(string content)
        {
            return FixStr(content, false);
        }

        /// <summary>
        /// Fix assembly reference in attribute
        /// </summary>
        /// <param name="content">string to search in</param>
        /// <returns>new string with references fixed</returns>
        internal string FixReferenceInIkvmAttribute(string content)
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

        internal string FixTypeName(string assemblyName, string typeName)
        {
            // TODO handle renames
            return typeName;
        }

        internal string FixAssemblyName(string assemblyName)
        {
            if (MergedAssemblies.Any(x => x.FullName == assemblyName))
            {
                // TODO no public key token !
                return TargetAssemblyDefinition.FullName;
            }
            return assemblyName;
        }

        private bool RemoveAttributes(string attrTypeName, Func<CustomAttribute, bool> predicate)
        {
            bool ret = false;
            foreach (var ass in MergedAssemblies)
            {
                for (int i = 0; i < ass.CustomAttributes.Count; )
                {
                    if (ass.CustomAttributes[i].AttributeType.FullName == attrTypeName && (predicate == null || predicate(ass.CustomAttributes[i])))
                    {
                        ass.CustomAttributes.RemoveAt(i);
                        ret = true;
                        continue;
                    }
                    i++;
                }
            }
            return ret;
        }

        private void CleanupAttributes(string type, Func<CustomAttribute, bool> extra)
        {
            if (!MergedAssemblies.All(ass => ass.CustomAttributes.Any(attr => attr.AttributeType.FullName == type && (extra == null || extra(attr)))))
            {
                if (RemoveAttributes(type, null))
                {
                    Logger.WARN("[" + type + "] attribute wasn't merged because of inconsistency accross merged assemblies");
                }
            }
        }

        private EmbeddedResource GetRepackListResource(List<string> repackList)
        {
            repackList.Sort();
            using (var stream = new MemoryStream())
            {
                new BinaryFormatter().Serialize(stream, repackList.ToArray());
                return new EmbeddedResource("ILRepack.List", ManifestResourceAttributes.Public, stream.ToArray());
            }
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

        public CustomAttributeArgument Copy(CustomAttributeArgument arg, IGenericParameterProvider context)
        {
            return new CustomAttributeArgument(_repackImporter.Import(arg.Type, context), ImportCustomAttributeValue(arg.Value, context));
        }

        public CustomAttributeNamedArgument Copy(CustomAttributeNamedArgument namedArg, IGenericParameterProvider context)
        {
            return new CustomAttributeNamedArgument(namedArg.Name, Copy(namedArg.Argument, context));
        }

        private object ImportCustomAttributeValue(object obj, IGenericParameterProvider context)
        {
            if (obj is TypeReference)
                return _repackImporter.Import((TypeReference)obj, context);
            if (obj is CustomAttributeArgument)
                return Copy((CustomAttributeArgument)obj, context);
            if (obj is CustomAttributeArgument[])
                return ((CustomAttributeArgument[])obj).Select(a => Copy(a, context)).ToArray();
            return obj;
        }

        /// <summary>
        /// Clones a collection of SecurityDeclarations
        /// </summary>
        public void CopySecurityDeclarations(Collection<SecurityDeclaration> input, Collection<SecurityDeclaration> output, IGenericParameterProvider context)
        {
            foreach (SecurityDeclaration sec in input)
            {
                SecurityDeclaration newSec = null;
                if (PermissionsetHelper.IsXmlPermissionSet(sec))
                {
                    newSec = PermissionsetHelper.Xml2PermissionSet(sec, TargetAssemblyMainModule);
                }
                if (newSec == null)
                {
                    newSec = new SecurityDeclaration(sec.Action);
                    foreach (SecurityAttribute sa in sec.SecurityAttributes)
                    {
                        SecurityAttribute newSa = new SecurityAttribute(_repackImporter.Import(sa.AttributeType, context));
                        if (sa.HasFields)
                        {
                            foreach (CustomAttributeNamedArgument cana in sa.Fields)
                            {
                                newSa.Fields.Add(Copy(cana, context));
                            }
                        }
                        if (sa.HasProperties)
                        {
                            foreach (CustomAttributeNamedArgument cana in sa.Properties)
                            {
                                newSa.Properties.Add(Copy(cana, context));
                            }
                        }
                        newSec.SecurityAttributes.Add(newSa);
                    }
                }
                output.Add(newSec);
            }
        }

        // helper
        private static void Copy<T>(Collection<T> input, Collection<T> output, Action<T, T> action)
        {
            if (input.Count != output.Count)
                throw new InvalidOperationException();
            for (int i = 0; i < input.Count; i++)
            {
                action.Invoke(input[i], output[i]);
            }
        }

        public void CopyGenericParameters(Collection<GenericParameter> input, Collection<GenericParameter> output, IGenericParameterProvider nt)
        {
            foreach (GenericParameter gp in input)
            {
                GenericParameter ngp = new GenericParameter(gp.Name, nt);

                ngp.Attributes = gp.Attributes;
                output.Add(ngp);
            }
            // delay copy to ensure all generics parameters are already present
            Copy(input, output, (gp, ngp) => CopyTypeReferences(gp.Constraints, ngp.Constraints, nt));
            Copy(input, output, (gp, ngp) => CopyCustomAttributes(gp.CustomAttributes, ngp.CustomAttributes, nt));
        }

        public void CopyCustomAttributes(Collection<CustomAttribute> input, Collection<CustomAttribute> output, IGenericParameterProvider context)
        {
            CopyCustomAttributes(input, output, true, context);
        }

        public CustomAttribute Copy(CustomAttribute ca, IGenericParameterProvider context)
        {
            CustomAttribute newCa = new CustomAttribute(_repackImporter.Import(ca.Constructor));
            foreach (var arg in ca.ConstructorArguments)
                newCa.ConstructorArguments.Add(Copy(arg, context));
            foreach (var arg in ca.Fields)
                newCa.Fields.Add(Copy(arg, context));
            foreach (var arg in ca.Properties)
                newCa.Properties.Add(Copy(arg, context));
            return newCa;
        }

        public void CopyCustomAttributes(Collection<CustomAttribute> input, Collection<CustomAttribute> output, bool allowMultiple, IGenericParameterProvider context)
        {
            foreach (CustomAttribute ca in input)
            {
                var caType = ca.AttributeType;
                var similarAttributes = output.Where(attr => ReflectionHelper.AreSame(attr.AttributeType, caType)).ToList();
                if (similarAttributes.Count != 0)
                {
                    if (!allowMultiple)
                        continue;
                    if (!CustomAttributeTypeAllowsMultiple(caType))
                        continue;
                    if (similarAttributes.Any(x =>
                            ReflectionHelper.AreSame(x.ConstructorArguments, ca.ConstructorArguments) &&
                            ReflectionHelper.AreSame(x.Fields, ca.Fields) &&
                            ReflectionHelper.AreSame(x.Properties, ca.Properties)
                        ))
                        continue;
                }
                output.Add(Copy(ca, context));
            }
        }

        private bool CustomAttributeTypeAllowsMultiple(TypeReference type)
        {
            if (type.FullName == "IKVM.Attributes.JavaModuleAttribute" || type.FullName == "IKVM.Attributes.PackageListAttribute")
            {
                // IKVM module attributes, although they don't allow multiple, IKVM supports the attribute being specified multiple times
                return true;
            }
            TypeDefinition typeDef = type.Resolve();
            if (typeDef != null)
            {
                var ca = typeDef.CustomAttributes.FirstOrDefault(x => x.AttributeType.FullName == "System.AttributeUsageAttribute");
                if (ca != null)
                {
                    var prop = ca.Properties.FirstOrDefault(y => y.Name == "AllowMultiple");
                    if (prop.Argument.Value is bool)
                    {
                        return (bool)prop.Argument.Value;
                    }
                }
            }
            // default is false
            return false;
        }

        public void CopyTypeReferences(Collection<TypeReference> input, Collection<TypeReference> output, IGenericParameterProvider context)
        {
            foreach (TypeReference ta in input)
            {
                output.Add(_repackImporter.Import(ta, context));
            }
        }

        public TypeDefinition GetMergedTypeFromTypeRef(TypeReference reference)
        {
            return MappingHandler.GetRemappedType(reference);
        }

        internal TypeReference CreateReference(ExportedType type)
        {
            return new TypeReference(type.Namespace, type.Name, TargetAssemblyMainModule, type.Scope)
            {
                DeclaringType = type.DeclaringType != null ? CreateReference(type.DeclaringType) : null,
            };
        }

        public TypeReference GetExportedTypeFromTypeRef(TypeReference type)
        {
            return MappingHandler.GetExportedRemappedType(type) ?? type;
        }
    }
}
