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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace ILRepacking
{
    public class ILRepack
    {
        // keep ILMerge syntax (both command-line & api) for compatibility (commented out: not implemented yet)

        public void AllowDuplicateType(string typeName)
        {
            allowedDuplicateTypes[typeName] = typeName;
        }
        public bool AllowDuplicateResources { get; set; }
        public bool AllowMultipleAssemblyLevelAttributes { get; set; }
        public bool AllowWildCards { get; set; }
        public bool AllowZeroPeKind { get; set; }
        public string AttributeFile { get; set; }
        public bool Closed { get; set; } // UNIMPL
        public bool CopyAttributes { get; set; }
        public bool DebugInfo { get; set; }
        public bool DelaySign { get; set; } // UNIMPL, how does this work with cecil?
        public string ExcludeFile { get; set; }
        public int FileAlignment { get; set; } // UNIMPL, not supported by cecil
        public string[] InputAssemblies { get; set; }
        public bool Internalize { get; set; }
        public string KeyFile { get; set; }
        public bool Log { get; set; }
        public string LogFile { get; set; }
        public void Merge()
        {
            Repack();
        }
        public string OutputFile { get; set; }
        public bool PublicKeyTokens { get; set; } // UNIMPL
        public void SetInputAssemblies(string[] inputAssemblies)
        {
            InputAssemblies = inputAssemblies;
        }
        public void SetSearchDirectories(string[] dirs)
        {
            foreach (var dir in dirs)
            {
                ((DefaultAssemblyResolver)GlobalAssemblyResolver.Instance).AddSearchDirectory(dir);
            }
        }
        public void SetTargetPlatform(string targetPlatformVersion, string targetPlatformDirectory)
        {
            TargetPlatformVersion = targetPlatformVersion;
            TargetPlatformDirectory = targetPlatformDirectory;
        }
        public bool StrongNameLost { get; private set; }
        public Kind? TargetKind { get; set; }
        public string TargetPlatformDirectory { get; set; } // UNIMPL, not supported by cecil?
        public string TargetPlatformVersion { get; set; } // TODO: not working yet
        public bool UnionMerge { get; set; }
        public Version Version { get; set; }
        public bool XmlDocumentation { get; set; }

        // end of ILMerge-similar attributes

        public bool LogVerbose { get; set; }

        internal List<string> MergedAssemblyFiles { get; set; }
        // contains all 'other' assemblies, but not the primary assembly
        internal List<AssemblyDefinition> OtherAssemblies { get; set; }
        // contains all assemblies, primary and 'other'
        internal List<AssemblyDefinition> MergedAssemblies { get; set; }
        internal AssemblyDefinition TargetAssemblyDefinition { get; set; }
        internal AssemblyDefinition PrimaryAssemblyDefinition { get; set; }

        // helpers
        internal ModuleDefinition TargetAssemblyMainModule { get { return TargetAssemblyDefinition.MainModule; } }

        private ModuleDefinition PrimaryAssemblyMainModule { get { return PrimaryAssemblyDefinition.MainModule; } }

        private StreamWriter logFile;

        private System.Collections.Hashtable allowedDuplicateTypes = new System.Collections.Hashtable();
        private List<System.Text.RegularExpressions.Regex> excludeInternalizeMatches;

        public ILRepack()
        {
            // default values
            LogVerbose = false;
        }

        private void AlwaysLog(object str)
        {
            string logStr = str.ToString();
            Console.WriteLine(logStr);
            if (logFile != null)
                logFile.WriteLine(logStr);
        }

        internal void LogOutput(object str)
        {
            if (Log)
            {
                AlwaysLog(str);
            }
        }

        private void InitializeLogFile()
        {
            if (!string.IsNullOrEmpty(LogFile))
            {
                Log = true;
                logFile = new StreamWriter(LogFile);
            }
        }

        private void CloseLogFile()
        {
            if (logFile != null)
            {
                logFile.Flush();
                logFile.Close();
                logFile.Dispose();
                logFile = null;
            }
        }

        internal void ERROR(string msg)
        {
            AlwaysLog("ERROR: " + msg);
        }

        internal void WARN(string msg)
        {
            AlwaysLog("WARN: " + msg);
        }

        internal void INFO(string msg)
        {
            LogOutput("INFO: " + msg);
        }

        internal void VERBOSE(string msg)
        {
            if (LogVerbose)
            {
                LogOutput("INFO: " + msg);
            }
        }

        internal void IGNOREDUP(string ignoredType, object ignoredObject)
        {
            // TODO: put on a list and log a summary
            //INFO("Ignoring duplicate " + ignoredType + " " + ignoredObject);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            ILRepack repack = new ILRepack();
            try
            {
                repack.ReadArguments(args);
                repack.Repack();
            }
            catch (Exception e)
            {
                repack.LogOutput(e);
                repack.CloseLogFile();
                return 1;
            }
            repack.CloseLogFile();
            return 0;
        }

        void Exit(int exitCode)
        {
            CloseLogFile();
            Environment.Exit(exitCode);
        }

        private void ReadArguments(string[] args)
        {
            CommandLine cmd = new CommandLine(args);
            if (cmd.Modifier("?") | cmd.Modifier("help") | cmd.Modifier("h") | args.Length == 0)
            {
                Usage();
                Exit(2);
            }
            AllowDuplicateResources = cmd.Modifier("allowduplicateresources");
            foreach (string dupType in cmd.Options("allowdup"))
                AllowDuplicateType(dupType);
            AllowMultipleAssemblyLevelAttributes = cmd.Modifier("allowmultiple");
            AllowWildCards = cmd.Modifier("wildcards");
            AllowZeroPeKind = cmd.Modifier("zeropekind");
            AttributeFile = cmd.Option("attr");
            Closed = cmd.Modifier("closed");
            CopyAttributes = cmd.Modifier("copyattrs");
            DebugInfo = !cmd.Modifier("ndebug");
            DelaySign = cmd.Modifier("delaysign");
            cmd.Option("align"); // not supported, just prevent interpreting this as file...
            Internalize = cmd.HasOption("internalize");
            if (Internalize)
            {
                // this file shall contain one regex per line to compare agains FullName of types NOT to internalize
                ExcludeFile = cmd.Option("internalize");
            }
            KeyFile = cmd.Option("keyfile");
            Log = cmd.HasOption("log");
            if (Log)
                LogFile = cmd.Option("log");
            OutputFile = cmd.Option("out");
            PublicKeyTokens = cmd.Modifier("usefullpublickeyforreferences");
            var targetKind = cmd.Option("target");
            if (string.IsNullOrEmpty(targetKind))
                targetKind = cmd.Option("t");
            if (!string.IsNullOrEmpty(targetKind))
            {
                switch (targetKind)
                {
                    case ("library"):
                        TargetKind = Kind.Dll;
                        break;
                    case ("exe"):
                        TargetKind = Kind.Exe;
                        break;
                    case ("winexe"):
                        TargetKind = Kind.WinExe;
                        break;
                    default:
                        Console.WriteLine("Invalid target: \"" + targetKind + "\".");
                        Usage();
                        Exit(2);
                        break;
                }
            }
            // TargetPlatformDirectory -> how does cecil handle that?
            TargetPlatformVersion = cmd.Option("targetplatform");
            if (cmd.Modifier("v1"))
                TargetPlatformVersion = "v1";
            if (cmd.Modifier("v1.1"))
                TargetPlatformVersion = "v1.1";
            if (cmd.Modifier("v2"))
                TargetPlatformVersion = "v2";
            if (cmd.Modifier("v4"))
                TargetPlatformVersion = "v4";
            UnionMerge = cmd.Modifier("union");
            var version = cmd.Option("ver");
            if (!string.IsNullOrEmpty(version))
                Version = new Version(version);
            XmlDocumentation = cmd.Modifier("xmldocs");

            SetSearchDirectories(cmd.Options("lib"));

            // private cmdline-options:
            LogVerbose = cmd.Modifier("verbose");

            if (string.IsNullOrEmpty(KeyFile) && DelaySign)
                WARN("Option 'delaysign' is only valid with 'keyfile'.");
            if (AllowMultipleAssemblyLevelAttributes && !CopyAttributes)
                WARN("Option 'allowMultiple' is only valid with 'copyattrs'.");
            if (!string.IsNullOrEmpty(AttributeFile) && (CopyAttributes))
                WARN("Option 'attr' can not be used with 'copyattrs'.");

            // everything that doesn't start with a '/' must be a file to merge (verify when loading the files)
            InputAssemblies = cmd.OtherAguments;
        }

        private void Usage()
        {
            Console.WriteLine(@"IL Repack - assembly merging using Mono.Cecil 0.9.4.0 - Version " + typeof(ILRepack).Assembly.GetName().Version.ToString(2));
            Console.WriteLine(@"Syntax: ILRepack.exe [options] /out:<path> <path_to_primary> [<other_assemblies> ...]");
            Console.WriteLine(@" - /help              displays this usage");
            Console.WriteLine(@" - /keyfile:<path>    specifies a keyfile to sign the output assembly");
            Console.WriteLine(@" - /log:[logfile]     enable logging (to a file, if given) (default is disabled)");
            Console.WriteLine(@" - /ver:M.X.Y.Z       target assembly version");
            Console.WriteLine(@" - /union             merges types with identical names into one");
            Console.WriteLine(@" - /ndebug            disables symbol file generation");
            Console.WriteLine(@" - /copyattrs         copy assembly attributes (by default only the primary assembly attributes are copied)");
            Console.WriteLine(@" - /attr:<path>       take assembly attributes from the given assembly file");
            Console.WriteLine(@" - /allowMultiple     when copyattrs is specified, allows multiple attributes (if type allows)");
            Console.WriteLine(@" - /target:kind       specify target assembly kind (library, exe, winexe supported, default is same as first assembly)");
            Console.WriteLine(@" - /targetplatform:P  specify target platform (v1, v1.1, v2, v4 supported)");
            Console.WriteLine(@" - /xmldocs           merges XML documentation as well");
            Console.WriteLine(@" - /lib:<path>        adds the path to the search directories for referenced assemblies (can be specified multiple times)");

            Console.WriteLine(@" - /usefullpublickeyforreferences [NOT IMPLEMENTED]");
            Console.WriteLine(@" - /internalize       [NOT IMPLEMENTED]");
            Console.WriteLine(@" - /delaysign         [NOT IMPLEMENTED]");
            Console.WriteLine(@" - /align             [NOT IMPLEMENTED]");
            Console.WriteLine(@" - /closed            [NOT IMPLEMENTED]");

            Console.WriteLine(@" - /allowdup:Type     allows the specified type for being duplicated in input assemblies");
            Console.WriteLine(@" - /allowduplicateresources allows to duplicate resources in output assembly (by default they're ignored)");
            Console.WriteLine(@" - /zeropekind        allows assemblies with Zero PeKind (but obviously only IL will get merged)");
            Console.WriteLine(@" - /wildcards         allows (and resolves) file wildcards (e.g. *.dll) in input assemblies");
            Console.WriteLine(@" - /verbose           shows more logs");
            Console.WriteLine(@" - /out:<path>        target assembly path, symbol/config/doc files will be written here as well");
            Console.WriteLine(@" - <path_to_primary>  primary assembly, gives the name, version to the merged one");
            Console.WriteLine(@" - <other_assemblies> ...");
            Console.WriteLine(@"");
            Console.WriteLine(@"Note: for compatibility purposes, all options can be specified using '/', '-' or '--' prefix.");
        }

        private void ReadInputAssemblies()
        {
            MergedAssemblyFiles = InputAssemblies.SelectMany(x => ResolveFile(x)).ToList();
            OtherAssemblies = new List<AssemblyDefinition>();
            // TODO: this could be parallelized to gain speed
            bool mergedDebugInfo = false;
            foreach (string assembly in MergedAssemblyFiles)
            {
                INFO("Adding assembly for merge: " + assembly);
                try
                {
                    ReaderParameters rp = new ReaderParameters(ReadingMode.Immediate);
                    // read PDB/MDB?
                    if (DebugInfo && (File.Exists(Path.ChangeExtension(assembly, "pdb")) || File.Exists(Path.ChangeExtension(assembly, "mdb"))))
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
                            INFO("Failed to load debug information for " + assembly);
                        }
                        else
                        {
                            throw;
                        }
                    }
                    if (!AllowZeroPeKind && (mergeAsm.MainModule.Attributes & ModuleAttributes.ILOnly) == 0)
                        throw new ArgumentException("Failed to load assembly with Zero PeKind: " + assembly);
                    else
                    {
                        if (rp.ReadSymbols)
                            mergedDebugInfo = true;
                        if (PrimaryAssemblyDefinition == null)
                            PrimaryAssemblyDefinition = mergeAsm;
                        else
                            OtherAssemblies.Add(mergeAsm);
                    }
                }
                catch
                {
                    ERROR("Failed to load assembly " + assembly);
                    throw;
                }
            }
            // prevent writing PDB if we haven't read any
            DebugInfo = mergedDebugInfo;

            MergedAssemblies = new List<AssemblyDefinition>(OtherAssemblies);
            MergedAssemblies.Add(PrimaryAssemblyDefinition);
        }

        private IEnumerable<string> ResolveFile(string s)
        {
            if (!AllowWildCards || s.IndexOfAny(new []{'*', '?'}) == -1)
                return new []{s};
            if (Path.GetDirectoryName(s).IndexOfAny(new[] { '*', '?' }) != -1)
                throw new Exception("Invalid path: " + s);
            string dir = Path.GetDirectoryName(s);
            if(String.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            return Directory.GetFiles(Path.GetFullPath(dir), Path.GetFileName(s));
        }

        public enum Kind
        {
            Dll,
            Exe,
            WinExe,
            SameAsPrimaryAssembly
        }

        /// <summary>
        /// Parse contents of properties: central point for checking (set on assembly or through command-line).
        /// </summary>
        private void ParseProperties()
        {
            if (string.IsNullOrEmpty(OutputFile))
            {
                throw new ArgumentException("No output file given.");
            }
            if ((InputAssemblies == null) || (InputAssemblies.Length == 0))
            {
                throw new ArgumentException("No input files given.");
            }

            if ((KeyFile != null) && !File.Exists(KeyFile))
            {
                throw new ArgumentException("KeyFile does not exist: \"" + KeyFile + "\".");
            }
            if (Internalize && !string.IsNullOrEmpty(ExcludeFile))
            {
                string[] lines = File.ReadAllLines(ExcludeFile);
                excludeInternalizeMatches = new List<System.Text.RegularExpressions.Regex>(lines.Length);
                foreach (string line in lines)
                    excludeInternalizeMatches.Add(new System.Text.RegularExpressions.Regex(line));
            }
        }

        /// <summary>
        /// Check if a type's FullName matches a Reges to exclude it from internalizing.
        /// </summary>
        private bool ExcludeInternalizeMatches(string typeFullName)
        {
            string withSquareBrackets = "[" + typeFullName + "]";
            foreach (System.Text.RegularExpressions.Regex r in excludeInternalizeMatches)
                if (r.IsMatch(typeFullName) || r.IsMatch(withSquareBrackets))
                    return true;
            return false;
        }

        /// <summary>
        /// The actual repacking process, called by main after parsing arguments.
        /// When referencing this assembly, call this after setting the merge properties.
        /// </summary>
        public void Repack()
        {
            InitializeLogFile();
            ParseProperties();
            // Read input assemblies only after all properties are set.
            ReadInputAssemblies();
            bool hadStrongName = PrimaryAssemblyDefinition.Name.HasPublicKey;

            ModuleKind kind = PrimaryAssemblyMainModule.Kind;
            if (TargetKind.HasValue)
            {
                switch (TargetKind.Value)
                {
                    case Kind.Dll: kind = ModuleKind.Dll; break;
                    case Kind.Exe: kind = ModuleKind.Console; break;
                    case Kind.WinExe: kind = ModuleKind.Windows; break;
                }
            }
            TargetRuntime runtime = PrimaryAssemblyMainModule.Runtime;
            if (TargetPlatformVersion != null)
            {
                switch (TargetPlatformVersion)
                {
                    case "v1": runtime = TargetRuntime.Net_1_0; break;
                    case "v1.1": runtime = TargetRuntime.Net_1_1; break;
                    case "v2": runtime = TargetRuntime.Net_2_0; break;
                    case "v4": runtime = TargetRuntime.Net_4_0; break;
                    default: throw new ArgumentException("Invalid TargetPlatformVersion: \"" + TargetPlatformVersion + "\".");
                }
            }

            // change assembly's name to correspond to the file we create
            string mainModuleName = Path.GetFileNameWithoutExtension(OutputFile);

            if (TargetAssemblyDefinition == null)
            {
                AssemblyNameDefinition asmName = Clone(PrimaryAssemblyDefinition.Name);
                asmName.Name = mainModuleName;
                TargetAssemblyDefinition = AssemblyDefinition.CreateAssembly(asmName, mainModuleName,
                    new ModuleParameters()
                        {
                            Kind = kind,
                            Architecture = PrimaryAssemblyMainModule.Architecture,
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

            if (Version != null)
                TargetAssemblyDefinition.Name.Version = Version;
            // TODO: Win32 version/icon properties seem not to be copied... limitation in cecil 0.9x?

            INFO("Processing references");
            // Add all AssemblyReferences to merged assembly (probably not necessary)
            foreach (var z in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.AssemblyReferences))
            {
                string name = z.Name;
                if (!MergedAssemblies.Any(y => y.Name.Name == name) &&
                    TargetAssemblyDefinition.Name.Name != name &&
                    !TargetAssemblyDefinition.MainModule.AssemblyReferences.Any(y => y.Name == name))
                {
                    // TODO: fix .NET runtime references?
                    // - to target a specific runtime version or
                    // - to target a single version if merged assemblies target different versions
                    VERBOSE("- add reference " + z);
                    TargetAssemblyDefinition.MainModule.AssemblyReferences.Add(z);
                }
            }

            // add all module references (pinvoke dlls)
            foreach (var z in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.ModuleReferences))
            {
                string name = z.Name;
                if (!TargetAssemblyDefinition.MainModule.ModuleReferences.Any(y => y.Name == name))
                {
                    TargetAssemblyDefinition.MainModule.ModuleReferences.Add(z);
                }
            }

            INFO("Processing resources");
            // merge resources
            foreach (var r in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.Resources))
            {
                if (!AllowDuplicateResources && TargetAssemblyDefinition.MainModule.Resources.Any(x => x.Name == r.Name))
                {
                    // Not much we can do about 'ikvm__META-INF!MANIFEST.MF'
                    // TODO: but might have to merge 'ikvm.exports'
                    VERBOSE("- Ignoring duplicate resource " + r.Name);
                }
                else
                {
                    VERBOSE("- Importing " + r.Name);
                    TargetAssemblyDefinition.MainModule.Resources.Add(r);
                }
            }

            INFO("Processing types");
            // merge types, this differs between 'primary' and 'other' assemblies regarding internalizing
            foreach (var r in PrimaryAssemblyDefinition.Modules.SelectMany(x => x.Types))
            {
                VERBOSE("- Importing " + r);
                Import(r, TargetAssemblyMainModule.Types, false);
            }
            foreach (var r in OtherAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.Types))
            {
                VERBOSE("- Importing " + r);
                bool internalize = Internalize;
                if (excludeInternalizeMatches != null)
                    internalize = !ExcludeInternalizeMatches(r.FullName);
                Import(r, TargetAssemblyMainModule.Types, internalize);
            }

            if (CopyAttributes)
            {
                foreach (var ass in MergedAssemblies)
                {
                    CopyCustomAttributes(ass.CustomAttributes, TargetAssemblyDefinition.CustomAttributes, AllowMultipleAssemblyLevelAttributes, null);
                }
                foreach (var mod in MergedAssemblies.SelectMany(x => x.Modules))
                {
                    CopyCustomAttributes(mod.CustomAttributes, TargetAssemblyMainModule.CustomAttributes, AllowMultipleAssemblyLevelAttributes, null);
                }
            }
            else if (AttributeFile != null)
            {
                AssemblyDefinition attributeAsm = AssemblyDefinition.ReadAssembly(AttributeFile);
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
            ReferenceFixator fixator = new ReferenceFixator(this);
            CopySecurityDeclarations(PrimaryAssemblyDefinition.SecurityDeclarations, TargetAssemblyDefinition.SecurityDeclarations, null);
            if (PrimaryAssemblyMainModule.EntryPoint != null)
            {
                TargetAssemblyMainModule.EntryPoint = fixator.Fix(Import(PrimaryAssemblyDefinition.EntryPoint), null).Resolve();
            }

            INFO("Fixing references");
            // this step travels through all TypeRefs & replaces them by matching TypeDefs

            foreach (var r in TargetAssemblyMainModule.Types)
            {
                fixator.FixReferences(r);
            }
            fixator.FixReferences(TargetAssemblyDefinition.CustomAttributes, null);
            fixator.FixReferences(TargetAssemblyDefinition.SecurityDeclarations, null);
            fixator.FixReferences(TargetAssemblyMainModule.CustomAttributes, null);

            // final reference cleanup (Cecil Import automatically added them)
            foreach (AssemblyDefinition asm in MergedAssemblies)
            {
                string mergedAssemblyName = asm.Name.Name;
                foreach (var refer in TargetAssemblyMainModule.AssemblyReferences.ToArray())
                {
                    // remove all referenced assemblies with same same, as we didn't bother on the version when merging
                    // in case we reference same assemblies with different versions, there might be prior errors if we don't merge the 'largest one'
                    if (refer.Name == mergedAssemblyName)
                    {
                        TargetAssemblyMainModule.AssemblyReferences.Remove(refer);
                    }
                }
            }

            INFO("Writing output assembly to disk");
            var parameters = new WriterParameters();
            if (KeyFile != null && File.Exists(KeyFile))
            {
                using (var stream = new FileStream(KeyFile, FileMode.Open))
                {
                    parameters.StrongNameKeyPair = new System.Reflection.StrongNameKeyPair(stream);
                }
            }
            // write PDB/MDB?
            if (DebugInfo)
                parameters.WriteSymbols = true;
            TargetAssemblyDefinition.Write(OutputFile, parameters);
            if (hadStrongName && !TargetAssemblyDefinition.Name.HasPublicKey)
                StrongNameLost = true;

            // nice to have, merge .config (assembly configuration file) & .xml (assembly documentation)
            ConfigMerger.Process(this);
            if(XmlDocumentation)
                DocumentationMerger.Process(this);
            
            // TODO: we're done here, the code below is only test code which can be removed once it's all running fine
            // 'verify' generated assembly
            AssemblyDefinition asm2 = AssemblyDefinition.ReadAssembly(OutputFile, new ReaderParameters(ReadingMode.Immediate));
            // lazy match on the name (not full) to catch requirements about merging different versions
            bool failed = false;
            foreach (var a in asm2.MainModule.AssemblyReferences.Where(x => MergedAssemblies.Any(y => x.Name == y.Name.Name)))
            {
                // failed
                ERROR("Merged assembly still references " + a.FullName);
                failed = true;
            }
            if (failed)
                throw new Exception("Merging failed, see above errors");
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

        /// <summary>
        /// Clones a field to a newly created type
        /// </summary>
        private void CloneTo(FieldDefinition field, TypeDefinition nt)
        {
            if (nt.Fields.Any(x => x.Name == field.Name))
            {
                IGNOREDUP("field", field);
                return;
            }
            FieldDefinition nf = new FieldDefinition(field.Name, field.Attributes, Import(field.FieldType, nt));
            nt.Fields.Add(nf);

            if (field.HasConstant)
                nf.Constant = field.Constant;

            if (field.HasMarshalInfo)
                nf.MarshalInfo = field.MarshalInfo;

            if (field.InitialValue != null && field.InitialValue.Length > 0)
                nf.InitialValue = field.InitialValue;

            if (field.HasLayoutInfo)
                nf.Offset = field.Offset;

            CopyCustomAttributes(field.CustomAttributes, nf.CustomAttributes, nt);
        }

        /// <summary>
        /// Clones a parameter into a newly created method
        /// </summary>
        private void CloneTo(ParameterDefinition param, MethodDefinition context, Collection<ParameterDefinition> col)
        {
            ParameterDefinition pd = new ParameterDefinition(param.Name, param.Attributes, Import(param.ParameterType, context));
            if (param.HasMarshalInfo)
                pd.MarshalInfo = param.MarshalInfo;
            col.Add(pd);
        }

        private CustomAttributeArgument Copy(CustomAttributeArgument arg, IGenericParameterProvider context)
        {
            return new CustomAttributeArgument(Import(arg.Type, context), arg.Value);
        }

        private CustomAttributeNamedArgument Copy(CustomAttributeNamedArgument namedArg, IGenericParameterProvider context)
        {
            return new CustomAttributeNamedArgument(namedArg.Name, Copy(namedArg.Argument, context));
        }

        /// <summary>
        /// Clones a collection of SecurityDeclarations
        /// </summary>
        private void CopySecurityDeclarations(Collection<SecurityDeclaration> input, Collection<SecurityDeclaration> output, IGenericParameterProvider context)
        {
            foreach (SecurityDeclaration sec in input)
            {
                SecurityDeclaration newSec = new SecurityDeclaration(sec.Action);
                foreach (SecurityAttribute sa in sec.SecurityAttributes)
                {
                    SecurityAttribute newSa = new SecurityAttribute(Import(sa.AttributeType, context));
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

        private void CopyGenericParameters(Collection<GenericParameter> input, Collection<GenericParameter> output, IGenericParameterProvider nt)
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

        private void CloneTo(EventDefinition evt, TypeDefinition nt, Collection<EventDefinition> col)
        {
            // ignore duplicate event
            if (nt.Events.Any(x => x.Name == evt.Name))
            {
                IGNOREDUP("event", evt);
                return;
            }

            EventDefinition ed = new EventDefinition(evt.Name, evt.Attributes, Import(evt.EventType, nt));
            col.Add(ed);
            if (evt.AddMethod != null)
                ed.AddMethod = nt.Methods.Where(x => x.FullName == evt.AddMethod.FullName).First();
            if (evt.RemoveMethod != null)
                ed.RemoveMethod = nt.Methods.Where(x => x.FullName == evt.RemoveMethod.FullName).First();
            if (evt.InvokeMethod != null)
                ed.InvokeMethod = nt.Methods.Where(x => x.FullName == evt.InvokeMethod.FullName).First();
            if (evt.HasOtherMethods)
            {
                // TODO
                throw new InvalidOperationException();
            }

            CopyCustomAttributes(evt.CustomAttributes, ed.CustomAttributes, nt);
        }

        private void CopyCustomAttributes(Collection<CustomAttribute> input, Collection<CustomAttribute> output, IGenericParameterProvider context)
        {
            CopyCustomAttributes(input, output, true, context);
        }

        private void CopyCustomAttributes(Collection<CustomAttribute> input, Collection<CustomAttribute> output, bool allowMultiple, IGenericParameterProvider context)
        {
            foreach (CustomAttribute ca in input)
            {
                var caType = ca.AttributeType;
                if ((allowMultiple /* && TODO: type allows multiple */) ||
                    !output.Any(attr => ReflectionHelper.AreSame(attr.AttributeType, caType)))
                {
                    CustomAttribute newCa = new CustomAttribute(Import(ca.Constructor));
                    foreach (var arg in ca.ConstructorArguments)
                        newCa.ConstructorArguments.Add(Copy(arg, context));
                    foreach (var arg in ca.Fields)
                        newCa.Fields.Add(Copy(arg, context));
                    foreach (var arg in ca.Fields)
                        newCa.Fields.Add(Copy(arg, context));
                    output.Add(newCa);

                }
            }
        }

        private void CopyTypeReferences(Collection<TypeReference> input, Collection<TypeReference> output, IGenericParameterProvider context)
        {
            foreach (TypeReference ta in input)
            {
                output.Add(Import(ta, context));
            }
        }

        private TypeReference Import(TypeReference reference, IGenericParameterProvider context)
        {
            TypeDefinition type = TargetAssemblyMainModule.GetType(reference.FullName);
            if (type != null)
                return type;

            if (context is MethodReference)
                return TargetAssemblyMainModule.Import(reference, (MethodReference)context);
            else if (context is TypeReference)
                return TargetAssemblyMainModule.Import(reference, (TypeReference)context);
            else if (context == null)
            {
                // we come here when importing types used for assembly-level custom attributes
                return TargetAssemblyMainModule.Import(reference);
            }
            throw new InvalidOperationException();
        }

        private FieldReference Import(FieldReference reference, IGenericParameterProvider context)
        {
            if (context is MethodReference)
                return TargetAssemblyMainModule.Import(reference, (MethodReference)context);
            if (context is TypeReference)
                return TargetAssemblyMainModule.Import(reference, (TypeReference)context);
            throw new InvalidOperationException();
        }

        private MethodReference Import(MethodReference reference)
        {
            return TargetAssemblyMainModule.Import(reference);
        }

        private MethodReference Import(MethodReference reference, IGenericParameterProvider context)
        {
            // If this is a Method/TypeDefinition, it will be corrected to a definition again later

            if (context is MethodReference)
                return TargetAssemblyMainModule.Import(reference, (MethodReference)context);
            if (context is TypeReference)
                return TargetAssemblyMainModule.Import(reference, (TypeReference)context);
            throw new InvalidOperationException();
        }

        private void CloneTo(PropertyDefinition prop, TypeDefinition nt, Collection<PropertyDefinition> col)
        {
            // ignore duplicate property
            if (nt.Properties.Any(x => x.Name == prop.Name))
            {
                IGNOREDUP("property", prop);
                return;
            }

            PropertyDefinition pd = new PropertyDefinition(prop.Name, prop.Attributes, Import(prop.PropertyType, nt));
            col.Add(pd);
            if (prop.SetMethod != null)
                pd.SetMethod = nt.Methods.Where(x => x.FullName == prop.SetMethod.FullName).First();
            if (prop.GetMethod != null)
                pd.GetMethod = nt.Methods.Where(x => x.FullName == prop.GetMethod.FullName).First();
            if (prop.HasOtherMethods)
            {
                // TODO
                throw new NotSupportedException("Property has other methods");
            }

            CopyCustomAttributes(prop.CustomAttributes, pd.CustomAttributes, nt);
        }

        private void CloneTo(MethodDefinition meth, TypeDefinition type)
        {
            // ignore duplicate method
            if (type.Methods.Any(x => (x.Name == meth.Name) && (x.Parameters.Count == meth.Parameters.Count) &&
                (x.ToString() == meth.ToString()))) // TODO: better/faster comparation of parameter types?
            {
                IGNOREDUP("method", meth);
                return;
            }
            // use void placeholder as we'll do the return type import later on (after generic parameters)
            MethodDefinition nm = new MethodDefinition(meth.Name, meth.Attributes, TargetAssemblyMainModule.TypeSystem.Void);
            nm.ImplAttributes = meth.ImplAttributes;

            type.Methods.Add(nm);

            CopyGenericParameters(meth.GenericParameters, nm.GenericParameters, nm);

            if (meth.HasPInvokeInfo)
            {
                nm.PInvokeInfo = new PInvokeInfo(meth.PInvokeInfo.Attributes, meth.PInvokeInfo.EntryPoint, meth.PInvokeInfo.Module);
            }

            foreach (ParameterDefinition param in meth.Parameters)
                CloneTo(param, nm, nm.Parameters);

            foreach (MethodReference ov in meth.Overrides)
                nm.Overrides.Add(Import(ov, nm));

            CopySecurityDeclarations(meth.SecurityDeclarations, nm.SecurityDeclarations, nm);
            CopyCustomAttributes(meth.CustomAttributes, nm.CustomAttributes, nm);

            nm.ReturnType = Import(meth.ReturnType, nm);
            if (meth.HasBody)
                CloneTo(meth.Body, nm);

            nm.IsAddOn = meth.IsAddOn;
            nm.IsRemoveOn = meth.IsRemoveOn;
            nm.IsGetter = meth.IsGetter;
            nm.IsSetter = meth.IsSetter;
            nm.CallingConvention = meth.CallingConvention;
        }

        private void CloneTo(MethodBody body, MethodDefinition parent)
        {
            MethodBody nb = new MethodBody(parent);
            parent.Body = nb;

            nb.MaxStackSize = body.MaxStackSize;
            nb.InitLocals = body.InitLocals;
            nb.LocalVarToken = body.LocalVarToken;

            foreach (VariableDefinition var in body.Variables)
                nb.Variables.Add(new VariableDefinition(
                    Import(var.VariableType, parent)));

            foreach (Instruction instr in body.Instructions)
            {
                Instruction ni;

                if (instr.OpCode.Code == Code.Calli)
                {
                    ni = Instruction.Create(instr.OpCode, (CallSite)instr.Operand);
                }
                else switch (instr.OpCode.OperandType)
                {
                    case OperandType.InlineArg:
                    case OperandType.ShortInlineArg:
                        if (instr.Operand == body.ThisParameter)
                        {
                            ni = Instruction.Create(instr.OpCode, nb.ThisParameter);
                        }
                        else
                        {
                            int param = body.Method.Parameters.IndexOf((ParameterDefinition)instr.Operand);
                            ni = Instruction.Create(instr.OpCode, parent.Parameters[param]);
                        }
                        break;
                    case OperandType.InlineVar:
                    case OperandType.ShortInlineVar:
                        int var = body.Variables.IndexOf((VariableDefinition)instr.Operand);
                        ni = Instruction.Create(instr.OpCode, nb.Variables[var]);
                        break;
                    case OperandType.InlineField:
                        ni = Instruction.Create(instr.OpCode, Import((FieldReference)instr.Operand, parent));
                        break;
                    case OperandType.InlineMethod:
                        ni = Instruction.Create(instr.OpCode, Import((MethodReference)instr.Operand, parent));
                        break;
                    case OperandType.InlineType:
                        ni = Instruction.Create(instr.OpCode, Import((TypeReference)instr.Operand, parent));
                        break;
                    case OperandType.InlineTok:
                        if (instr.Operand is TypeReference)
                            ni = Instruction.Create(instr.OpCode, Import((TypeReference)instr.Operand, parent));
                        else if (instr.Operand is FieldReference)
                            ni = Instruction.Create(instr.OpCode, Import((FieldReference)instr.Operand, parent));
                        else if (instr.Operand is MethodReference)
                            ni = Instruction.Create(instr.OpCode, Import((MethodReference)instr.Operand, parent));
                        else
                            throw new InvalidOperationException();
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.InlineBrTarget:
                        ni = Instruction.Create(instr.OpCode, (Instruction)instr.Operand); // TODO review
                        break;
                    case OperandType.InlineSwitch:
                        ni = Instruction.Create(instr.OpCode, (Instruction[])instr.Operand); // TODO review
                        break;
                    case OperandType.InlineR:
                        ni = Instruction.Create(instr.OpCode, (double)instr.Operand);
                        break;
                    case OperandType.ShortInlineR:
                        ni = Instruction.Create(instr.OpCode, (float)instr.Operand);
                        break;
                    case OperandType.InlineNone:
                        ni = Instruction.Create(instr.OpCode);
                        break;
                    case OperandType.InlineString:
                        ni = Instruction.Create(instr.OpCode, (string)instr.Operand);
                        break;
                    case OperandType.ShortInlineI:
                        if (instr.OpCode == OpCodes.Ldc_I4_S)
                            ni = Instruction.Create(instr.OpCode, (sbyte)instr.Operand);
                        else
                            ni = Instruction.Create(instr.OpCode, (byte)instr.Operand);
                        break;
                    case OperandType.InlineI8:
                        ni = Instruction.Create(instr.OpCode, (long)instr.Operand);
                        break;
                    case OperandType.InlineI:
                        ni = Instruction.Create(instr.OpCode, (int)instr.Operand);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
                ni.SequencePoint = instr.SequencePoint;
                nb.Instructions.Add(ni);
            }

            for (int i = 0; i < body.Instructions.Count; i++)
            {
                Instruction instr = nb.Instructions[i];
                if (instr.OpCode.OperandType != OperandType.ShortInlineBrTarget &&
                    instr.OpCode.OperandType != OperandType.InlineBrTarget)
                    continue;

                instr.Operand = GetInstruction(body, nb, (Instruction)body.Instructions[i].Operand);
            }

            foreach (ExceptionHandler eh in body.ExceptionHandlers)
            {
                ExceptionHandler neh = new ExceptionHandler(eh.HandlerType);
                neh.TryStart = GetInstruction(body, nb, eh.TryStart);
                neh.TryEnd = GetInstruction(body, nb, eh.TryEnd);
                neh.HandlerStart = GetInstruction(body, nb, eh.HandlerStart);
                neh.HandlerEnd = GetInstruction(body, nb, eh.HandlerEnd);

                switch (eh.HandlerType)
                {
                    case ExceptionHandlerType.Catch:
                        neh.CatchType = Import(eh.CatchType, parent);
                        break;
                    case ExceptionHandlerType.Filter:
                        neh.FilterStart = GetInstruction(body, nb, eh.FilterStart);
                        neh.FilterEnd = GetInstruction(body, nb, eh.FilterEnd);
                        break;
                }

                nb.ExceptionHandlers.Add(neh);
            }
        }

        internal static Instruction GetInstruction(MethodBody oldBody, MethodBody newBody, Instruction i)
        {
            int pos = oldBody.Instructions.IndexOf(i);
            if (pos > -1 && pos < newBody.Instructions.Count)
                return newBody.Instructions[pos];

            return null /*newBody.Instructions.Outside*/;
        }

        internal void Import(TypeDefinition type, Collection<TypeDefinition> col, bool internalize)
        {
            TypeDefinition nt = TargetAssemblyMainModule.GetType(type.FullName);
            if (nt == null)
            {
                nt = new TypeDefinition(type.Namespace, type.Name, type.Attributes);
                col.Add(nt);
                // only top-level types are internalized
                if (internalize && (nt.DeclaringType == null) && nt.IsPublic)
                    nt.IsPublic = false;

                CopyGenericParameters(type.GenericParameters, nt.GenericParameters, nt);
                if (type.BaseType != null)
                    nt.BaseType = Import(type.BaseType, nt);

                if (type.HasLayoutInfo)
                {
                    nt.ClassSize = type.ClassSize;
                    nt.PackingSize = type.PackingSize;
                }
                // don't copy these twice if UnionMerge==true
                // TODO: we can move this down if we chek for duplicates when adding
                CopySecurityDeclarations(type.SecurityDeclarations, nt.SecurityDeclarations, nt);
                CopyTypeReferences(type.Interfaces, nt.Interfaces, nt);
                CopyCustomAttributes(type.CustomAttributes, nt.CustomAttributes, nt);
            }
            else if (UnionMerge || DuplicateTypeAllowed(type.FullName))
            {
                INFO("Merging " + type);
            }
            else
            {
                ERROR("Duplicate type " + type);
                throw new InvalidOperationException("Duplicate type " + type);
            }

            // nested types first (are never internalized)
            foreach (TypeDefinition nested in type.NestedTypes)
                Import(nested, nt.NestedTypes, false);
            foreach (FieldDefinition field in type.Fields)
                CloneTo(field, nt);

            // methods before fields / events
            foreach (MethodDefinition meth in type.Methods)
                CloneTo(meth, nt);

            foreach (EventDefinition evt in type.Events)
                CloneTo(evt, nt, nt.Events);
            foreach (PropertyDefinition prop in type.Properties)
                CloneTo(prop, nt, nt.Properties);
        }

        private bool DuplicateTypeAllowed(string fullName)
        {
            // Merging module because IKVM uses this class to store some fields.
            // Doesn't fully work yet, as IKVM is nice enough to give all the fields the same name...
            if (fullName == "<Module>")
                return true;

            // Merge should be OK since member's names are pretty unique,
            // but renaming duplicate members would be safer...
            if (fullName == "<PrivateImplementationDetails>")
                return true;

            if (allowedDuplicateTypes.Contains(fullName))
                return true;

            return false;
        }
    }
}
