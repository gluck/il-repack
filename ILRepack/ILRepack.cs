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
    public class ILRepack : IRepackContext, IRepackCopier
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

        private readonly IRepackImporter _repackImporter;

        public ILRepack(RepackOptions options)
        {
            Options = options;
            Logger = options.Logger;
            _repackImporter = new RepackImporter(options.Logger, Options, this, this, aspOffsets);
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


        protected TargetRuntime ParseTargetPlatform()
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
            var TargetPlatformDirectory = Options.TargetPlatformDirectory ?? ResolveTargetPlatformDirectory(runtime);
            var dirs = Options.SearchDirectories;
            if (TargetPlatformDirectory != null)
            {
                var monoFacadesDirectory = Path.Combine(TargetPlatformDirectory, "Facades");
                var platformDirectories = Directory.Exists(monoFacadesDirectory) ?
                    new[] { TargetPlatformDirectory, monoFacadesDirectory } :
                    new[] { TargetPlatformDirectory };
                dirs = dirs.Concat(platformDirectories);
            }
            foreach (var dir in dirs)
            {
                GlobalAssemblyResolver.AddSearchDirectory(dir);
            }
            return runtime;
        }

        private string ResolveTargetPlatformDirectory(TargetRuntime runtime)
        {
            var platformBasePath = Path.GetDirectoryName(Path.GetDirectoryName(typeof(string).Assembly.Location));
            List<string> platformDirectories = new List<string>(Directory.GetDirectories(platformBasePath));
            var platformDir = runtime.ToString().Substring(4).Replace('_', '.');
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
            _reflectionHelper = new ReflectionHelper(this);
            Options.ParseProperties();

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
                new AttributesRepackStep(Logger, this, this, Options),
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
                return Array.ConvertAll((CustomAttributeArgument[])obj, a => Copy(a, context));
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
                var similarAttributes = output.Where(attr => _reflectionHelper.AreSame(attr.AttributeType, caType)).ToList();
                if (similarAttributes.Count != 0)
                {
                    if (!allowMultiple)
                        continue;
                    if (!CustomAttributeTypeAllowsMultiple(caType))
                        continue;
                    if (similarAttributes.Any(x =>
                            _reflectionHelper.AreSame(x.ConstructorArguments, ca.ConstructorArguments) &&
                            _reflectionHelper.AreSame(x.Fields, ca.Fields) &&
                            _reflectionHelper.AreSame(x.Properties, ca.Properties)
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
            return _mappingHandler.GetRemappedType(reference);
        }

        public TypeReference GetExportedTypeFromTypeRef(TypeReference type)
        {
            return _mappingHandler.GetExportedRemappedType(type) ?? type;
        }
    }
}
