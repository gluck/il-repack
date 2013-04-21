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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.PE;
using Mono.Collections.Generic;
using Mono.Unix.Native;
using CustomAttributeNamedArgument = Mono.Cecil.CustomAttributeNamedArgument;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using System.Threading;

namespace ILRepacking
{
    public class RepackAssemblyResolver : DefaultAssemblyResolver
    {
        public void RegisterAssemblies(List<AssemblyDefinition> mergedAssemblies)
        {
            foreach (var assemblyDefinition in mergedAssemblies)
            {
                base.RegisterAssembly(assemblyDefinition);
            }
        }
    }


    public class ILRepack
    {
        // keep ILMerge syntax (both command-line & api) for compatibility (commented out: not implemented yet)

        public void AllowDuplicateType(string typeName)
        {
            if (typeName.EndsWith(".*"))
            {
                allowedDuplicateNameSpaces.Add(typeName.Substring(0, typeName.Length-2));
            }
            else
            {
                allowedDuplicateTypes[typeName] = typeName;
            }
        }
        public bool AllowDuplicateResources { get; set; }
        public bool AllowMultipleAssemblyLevelAttributes { get; set; }
        public bool AllowWildCards { get; set; }
        public bool AllowZeroPeKind { get; set; }
        public string AttributeFile { get; set; }
        public bool Closed { get; set; } // UNIMPL
        public bool CopyAttributes { get; set; }
        public bool DebugInfo { get; set; }
        public bool DelaySign { get; set; }
        public string ExcludeFile { get; set; }
        public int FileAlignment { get; set; } // UNIMPL, not supported by cecil
        public string[] InputAssemblies { get; set; }
        public bool Internalize { get; set; }
        public string KeyFile { get; set; }
        public bool Parallel { get; set; }
        public bool PauseBeforeExit { get; set; }
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
                globalAssemblyResolver.AddSearchDirectory(dir);
            }
        }
        public void SetTargetPlatform(string targetPlatformVersion, string targetPlatformDirectory)
        {
            TargetPlatformVersion = targetPlatformVersion;
            TargetPlatformDirectory = targetPlatformDirectory;
        }
        public bool StrongNameLost { get; private set; }
        public Kind? TargetKind { get; set; }
        public string TargetPlatformDirectory { get; set; }
        public string TargetPlatformVersion { get; set; }
        public bool UnionMerge { get; set; }
        public Version Version { get; set; }
        public bool XmlDocumentation { get; set; }

        // end of ILMerge-similar attributes

        public bool LogVerbose { get; set; }
        public bool NoRepackRes { get; set; }
        public bool KeepOtherVersionReferences { get; set; }
        public bool LineIndexation { get; set; }
        public string PrimaryAssemblyFile { get; set; }

        internal List<string> MergedAssemblyFiles { get; set; }
        // contains all 'other' assemblies, but not the primary assembly
        internal List<AssemblyDefinition> OtherAssemblies { get; set; }
        // contains all assemblies, primary and 'other'
        internal List<AssemblyDefinition> MergedAssemblies { get; set; }
        internal AssemblyDefinition TargetAssemblyDefinition { get; set; }
        internal AssemblyDefinition PrimaryAssemblyDefinition { get; set; }
        internal IKVMLineIndexer LineIndexer { get; set; }

        // helpers
        internal ModuleDefinition TargetAssemblyMainModule { get { return TargetAssemblyDefinition.MainModule; } }

        private ModuleDefinition PrimaryAssemblyMainModule { get { return PrimaryAssemblyDefinition.MainModule; } }

        private StreamWriter logFile;
        internal readonly RepackAssemblyResolver globalAssemblyResolver = new RepackAssemblyResolver();

        private readonly Hashtable allowedDuplicateTypes = new Hashtable();
        private readonly List<string> allowedDuplicateNameSpaces = new List<string>();
        private List<Regex> excludeInternalizeMatches;
        private ReflectionHelper reflectionHelper;
        private static Regex TYPE_RE = new Regex("^(.*?), ([^>,]+), .*$");

        private PlatformFixer platformFixer;
        private HashSet<string> mergeAsmNames;
        private MappingHandler mappingHandler;
        private readonly Dictionary<AssemblyDefinition, int> aspOffsets = new Dictionary<AssemblyDefinition, int>();

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
            int rc = -1;
            try
            {
                repack.ReadArguments(args);
                repack.Repack();
                rc = 0;
            }
            catch (Exception e)
            {
                repack.AlwaysLog(e);
                rc = 1;
            }
            finally
            {
                repack.CloseLogFile();
                if (repack.PauseBeforeExit)
                {
                  Console.WriteLine("Press Any Key To Continue");
                  Console.ReadKey(true);            
                }
            }
            return rc;
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
            Parallel = cmd.Modifier("parallel");
            PauseBeforeExit = cmd.Modifier("pause");
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
            var targetPlatform = cmd.Option("targetplatform");
            if (targetPlatform != null)
            {
                int dirIndex = targetPlatform.IndexOf(',');
                if (dirIndex != -1)
                {
                    TargetPlatformDirectory = targetPlatform.Substring(dirIndex + 1);
                    TargetPlatformVersion = targetPlatform.Substring(0, dirIndex);
                }
                else
                {
                    TargetPlatformVersion = targetPlatform;
                }
            }
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
            NoRepackRes = cmd.Modifier("norepackres");
            KeepOtherVersionReferences = cmd.Modifier("keepotherversionreferences");

            SetSearchDirectories(cmd.Options("lib"));

            // private cmdline-options:
            LogVerbose = cmd.Modifier("verbose");
            LineIndexation = cmd.Modifier("index");

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
            Console.WriteLine(@" - /log:<logfile>     enable logging (to a file, if given) (default is disabled)");
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
            Console.WriteLine(@" - /internalize       sets all types but the ones from the first assembly 'internal'");
            Console.WriteLine(@" - /delaysign         sets the key, but don't sign the assembly");
            Console.WriteLine(@" - /noRepackRes       do not add the resource 'ILRepack.List with all merged assembly names");
            
            Console.WriteLine(@" - /usefullpublickeyforreferences - NOT IMPLEMENTED");
            Console.WriteLine(@" - /align             - NOT IMPLEMENTED");
            Console.WriteLine(@" - /closed            - NOT IMPLEMENTED");
            
            Console.WriteLine(@" - /allowdup:Type     allows the specified type for being duplicated in input assemblies");
            Console.WriteLine(@" - /allowduplicateresources allows to duplicate resources in output assembly (by default they're ignored)");
            Console.WriteLine(@" - /zeropekind        allows assemblies with Zero PeKind (but obviously only IL will get merged)");
            Console.WriteLine(@" - /wildcards         allows (and resolves) file wildcards (e.g. *.dll) in input assemblies");
            Console.WriteLine(@" - /parallel          use as many CPUs as possible to merge the assemblies");
            Console.WriteLine(@" - /pause             pause execution once completed (good for debugging)");
            Console.WriteLine(@" - /index             stores file:line debug information as type/method attributes (requires PDB)");
            Console.WriteLine(@" - /verbose           shows more logs");
            Console.WriteLine(@" - /out:<path>        target assembly path, symbol/config/doc files will be written here as well");
            Console.WriteLine(@" - <path_to_primary>  primary assembly, gives the name, version to the merged one");
            Console.WriteLine(@" - <other_assemblies> ...");
            Console.WriteLine(@"");
            Console.WriteLine(@"Note: for compatibility purposes, all options are case insensitive, and can be specified using '/', '-' or '--' prefix.");
        }

        private void ReadInputAssemblies()
        {
            MergedAssemblyFiles = InputAssemblies.SelectMany(x => ResolveFile(x)).Distinct().ToList();
            OtherAssemblies = new List<AssemblyDefinition>();
            // TODO: this could be parallelized to gain speed
            bool mergedDebugInfo = false;
            foreach (string assembly in MergedAssemblyFiles)
            {
                INFO("Adding assembly for merge: " + assembly);
                try
                {
                    ReaderParameters rp = new ReaderParameters(ReadingMode.Immediate) { AssemblyResolver = globalAssemblyResolver };
                    // read PDB/MDB?
                    if (DebugInfo && (File.Exists(Path.ChangeExtension(assembly, "pdb")) || File.Exists(assembly + ".mdb")))
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
                    
                    if (rp.ReadSymbols)
                        mergedDebugInfo = true;
                    if (PrimaryAssemblyDefinition == null) {
                        PrimaryAssemblyDefinition = mergeAsm;
                        if(string.IsNullOrEmpty(PrimaryAssemblyFile)) {
                            PrimaryAssemblyFile = assembly;
                        }
                    }
                    else
                        OtherAssemblies.Add(mergeAsm);
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

        private void ReadInputAssembliesParallel()
        {
            INFO("Reading in Parallel");
            MergedAssemblyFiles = InputAssemblies.SelectMany(x => ResolveFile(x)).ToList();

            // TODO: this could be parallelized to gain speed
            bool mergedDebugInfo = false;
            AssemblyDefinition[] readAsms = new AssemblyDefinition[MergedAssemblyFiles.Count];
            int remain = MergedAssemblyFiles.Count;
            EventWaitHandle evt = new ManualResetEvent(false);
            for (int i = 0; i < MergedAssemblyFiles.Count; i++)
            {
                int idx = i;
                string assembly = MergedAssemblyFiles[idx];
                ThreadPool.QueueUserWorkItem((WaitCallback)((_) =>
                {
                    INFO("Adding assembly for merge: " + assembly);
                    try
                    {
                        ReaderParameters rp = new ReaderParameters(ReadingMode.Immediate) { AssemblyResolver = globalAssemblyResolver };
                        // read PDB/MDB?
                        if (DebugInfo && (File.Exists(Path.ChangeExtension(assembly, "pdb")) || File.Exists(assembly + ".mdb")))
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

                        if (rp.ReadSymbols)
                            mergedDebugInfo = true;
                        readAsms[idx] = mergeAsm;
                    }
                    catch
                    {
                        ERROR("Failed to load assembly " + assembly);
                        throw;
                    }
                    finally
                    {
                        if (Interlocked.Decrement(ref remain) == 0)
                            evt.Set();
                    }
                }));
            }

            evt.WaitOne();

            // prevent writing PDB if we haven't read any
            DebugInfo = mergedDebugInfo;

            MergedAssemblies = new List<AssemblyDefinition>(readAsms);
            PrimaryAssemblyDefinition = readAsms[0];
            OtherAssemblies = new List<AssemblyDefinition>(readAsms);
            OtherAssemblies.RemoveAt(0);
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
                excludeInternalizeMatches = new List<Regex>(lines.Length);
                foreach (string line in lines)
                    excludeInternalizeMatches.Add(new Regex(line));
            }
        }

        protected TargetRuntime ParseTargetPlatform()
        {
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
                platformFixer.ParseTargetPlatformDirectory(runtime, TargetPlatformDirectory);
            }
            return runtime;
        }

        /// <summary>
        /// Check if a type's FullName matches a Reges to exclude it from internalizing.
        /// </summary>
        private bool ShouldInternalize(string typeFullName)
        {
            if (excludeInternalizeMatches == null)
            {
                return Internalize;
            }
            string withSquareBrackets = "[" + typeFullName + "]";
            foreach (Regex r in excludeInternalizeMatches)
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
            reflectionHelper = new ReflectionHelper(this);
            InitializeLogFile();
            ParseProperties();
            // Read input assemblies only after all properties are set.
            if (Parallel)  
                ReadInputAssembliesParallel();
            else
                ReadInputAssemblies();
            globalAssemblyResolver.RegisterAssemblies(MergedAssemblies);
            var asmNames = KeepOtherVersionReferences ? 
              MergedAssemblies.Select(x => x.FullName) : 
              MergedAssemblies.Select(x => x.Name.Name);
          
            mergeAsmNames = new HashSet<string>(asmNames);
            platformFixer = new PlatformFixer(PrimaryAssemblyMainModule.Runtime);
            mappingHandler = new MappingHandler();
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
            TargetRuntime runtime = ParseTargetPlatform();

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
                            AssemblyResolver = globalAssemblyResolver,
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

            if (Version != null)
                TargetAssemblyDefinition.Name.Version = Version;
            // TODO: Win32 version/icon properties seem not to be copied... limitation in cecil 0.9x?
            StrongNameKeyPair snkp = null;
            if (KeyFile != null && File.Exists(KeyFile))
            {
                using (var stream = new FileStream(KeyFile, FileMode.Open, FileAccess.Read, FileShare.Read))
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
                TargetAssemblyMainModule.EntryPoint = fixator.Fix(Import(PrimaryAssemblyDefinition.EntryPoint)).Resolve();
            }

            INFO("Fixing references");
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
                    if (KeepOtherVersionReferences ? refer.FullName == asm.FullName : refer.Name == asm.Name.Name)
                    {
                        TargetAssemblyMainModule.AssemblyReferences.Remove(refer);
                    }
                }
            }

            INFO("Writing output assembly to disk");
            var parameters = new WriterParameters();
            if ((snkp != null) && !DelaySign)
                parameters.StrongNameKeyPair = snkp;
            // write PDB/MDB?
            if (DebugInfo)
                parameters.WriteSymbols = true;
            TargetAssemblyDefinition.Write(OutputFile, parameters);
            // If this is an executable and we are on linux/osx we should copy file permissions from
            // the primary assembly
            if (Environment.OSVersion.Platform == PlatformID.MacOSX || Environment.OSVersion.Platform == PlatformID.Unix) {
                Stat stat;
                INFO("Copying permissions from " + PrimaryAssemblyFile);
                Syscall.stat(PrimaryAssemblyFile, out stat);
                Syscall.chmod(OutputFile, stat.st_mode);
            }
            if (hadStrongName && !TargetAssemblyDefinition.Name.HasPublicKey)
                StrongNameLost = true;

            // nice to have, merge .config (assembly configuration file) & .xml (assembly documentation)
            ConfigMerger.Process(this);
            if(XmlDocumentation)
                DocumentationMerger.Process(this);
            
            // TODO: we're done here, the code below is only test code which can be removed once it's all running fine
            // 'verify' generated assembly
            AssemblyDefinition asm2 = AssemblyDefinition.ReadAssembly(OutputFile, new ReaderParameters(ReadingMode.Immediate) { AssemblyResolver = globalAssemblyResolver });
            // lazy match on the name (not full) to catch requirements about merging different versions
            bool failed = false;
            foreach (var a in asm2.MainModule.AssemblyReferences.Where(x => MergedAssemblies.Any(y => KeepOtherVersionReferences ? x.FullName == y.FullName : x.Name == y.Name.Name)))
            {
                // failed
                ERROR("Merged assembly still references " + a.FullName);
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
                    WARN(string.Format("Duplicate Win32 resource with id={0}, parents=[{1}], name={2} in assembly {3}, ignoring", entry.Id, string.Join(",", parents.Select(p => p.Name ?? p.Id.ToString()).ToArray()), entry.Name, ass.Name));
                }
                return;
            }
            if (exist.Data != null || entry.Data != null)
            {
                WARN("Inconsistent Win32 resources, ignoring");
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
            if (CopyAttributes)
            {
                CleanupAttributes(typeof (CompilationRelaxationsAttribute).FullName, x => x.ConstructorArguments.Count == 1 /* TODO && x.ConstructorArguments[0].Value.Equals(1) */);
                CleanupAttributes(typeof (SecurityTransparentAttribute).FullName, null);
                CleanupAttributes(typeof (SecurityCriticalAttribute).FullName, x => x.ConstructorArguments.Count == 0);
                CleanupAttributes(typeof (AllowPartiallyTrustedCallersAttribute).FullName, x => x.ConstructorArguments.Count == 0);
                CleanupAttributes("System.Security.SecurityRulesAttribute", x => x.ConstructorArguments.Count == 0);
                RemoveAttributes(typeof (InternalsVisibleToAttribute).FullName, ca =>
                                                                                {
                                                                                    String name = (string)ca.ConstructorArguments[0].Value;
                                                                                    int idx;
                                                                                    if ((idx = name.IndexOf(", PublicKey=")) != -1)
                                                                                    {
                                                                                        name = name.Substring(0, idx);
                                                                                    }
                                                                                    return MergedAssemblies.Any(x => x.Name.Name == name);
                                                                                });
                RemoveAttributes(typeof (AssemblyDelaySignAttribute).FullName, null);
                RemoveAttributes(typeof (AssemblyKeyFileAttribute).FullName, null);
                RemoveAttributes(typeof (AssemblyKeyNameAttribute).FullName, null);
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
                AssemblyDefinition attributeAsm = AssemblyDefinition.ReadAssembly(AttributeFile, new ReaderParameters(ReadingMode.Immediate) { AssemblyResolver = globalAssemblyResolver });
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
            INFO("Processing types");
            // merge types, this differs between 'primary' and 'other' assemblies regarding internalizing
            foreach (var r in PrimaryAssemblyDefinition.Modules.SelectMany(x => x.Types))
            {
                VERBOSE("- Importing " + r);
                Import(r, TargetAssemblyMainModule.Types, false);
            }
            foreach (var m in OtherAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.Types)
                {
                    VERBOSE("- Importing " + r);
                    Import(r, TargetAssemblyMainModule.Types, ShouldInternalize(r.FullName));
                }
            }
        }

        private void RepackExportedTypes()
        {
            INFO("Processing types");
            foreach (var m in MergedAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                {
                    mappingHandler.StoreExportedType(m, r.FullName, CreateReference(r));
                }
            }
            foreach (var r in PrimaryAssemblyDefinition.Modules.SelectMany(x => x.ExportedTypes))
            {
                VERBOSE("- Importing Exported Type" + r);
                Import(r, TargetAssemblyMainModule.ExportedTypes, TargetAssemblyMainModule);
            }
            foreach (var m in OtherAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                {
                    if (!ShouldInternalize(r.FullName))
                    {
                        VERBOSE("- Importing Exported Type " + r);
                        Import(r, TargetAssemblyMainModule.ExportedTypes, TargetAssemblyMainModule);
                    }
                    else
                    {
                        VERBOSE("- Skipping Exported Type " + r);
                    }
                }
            }
        }

        private void RepackReferences()
        {
            INFO("Processing references");
            // Add all AssemblyReferences to merged assembly (probably not necessary)
            foreach (var z in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.AssemblyReferences))
            {
                string name = z.Name;
                if (!MergedAssemblies.Any(y => y.Name.Name == name) && TargetAssemblyDefinition.Name.Name != name && !TargetAssemblyMainModule.AssemblyReferences.Any(y => y.Name == name))
                {
                    // TODO: fix .NET runtime references?
                    // - to target a specific runtime version or
                    // - to target a single version if merged assemblies target different versions
                    VERBOSE("- add reference " + z);
                    AssemblyNameReference fixedRef = platformFixer.FixPlatformVersion(z);
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
            INFO("Processing resources");
            // merge resources
            List<string> repackList = null;
            EmbeddedResource repackListRes = null;
            Dictionary<string, List<int>> ikvmExportsLists = null;
            EmbeddedResource ikvmExports = null;
            if (!NoRepackRes)
            {
                repackList = MergedAssemblies.Select(a => a.FullName).ToList();
                repackListRes = GetRepackListResource(repackList);
                TargetAssemblyMainModule.Resources.Add(repackListRes);
            }
            foreach (var r in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.Resources))
            {
                if (r.Name == "ILRepack.List")
                {
                    if (!NoRepackRes && r is EmbeddedResource)
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
                    if (!AllowDuplicateResources && TargetAssemblyMainModule.Resources.Any(x => x.Name == r.Name))
                    {
                        // Not much we can do about 'ikvm__META-INF!MANIFEST.MF'
                        WARN("Ignoring duplicate resource " + r.Name);
                    }
                    else
                    {
                        VERBOSE("- Importing " + r.Name);
                        var nr = r;
                        switch (r.ResourceType)
                        {
                            case ResourceType.AssemblyLinked:
                                // TODO
                                WARN("AssemblyLinkedResource reference may need to be fixed (to link to newly created assembly)" + r.Name);
                                break;
                            case ResourceType.Linked:
                                // TODO ? (or not)
                                break;
                            case ResourceType.Embedded:
                                var er = (EmbeddedResource) r;
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
            MemoryStream stream = (MemoryStream) er.GetResourceStream();
            var output = new MemoryStream((int) stream.Length);
            var rw = new ResourceWriter(output);
            using (var rr = new ResReader(stream))
            {
                foreach (var res in rr)
                {
                    if (res.type == "ResourceTypeCode.String" || res.type.StartsWith("System.String"))
                    {
                        string content = (string) rr.GetObject(res);
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
            if (String.IsNullOrEmpty(content) || content.Length > 512 || content.IndexOf(", ") == -1 || content.StartsWith("System."))
                return content;
            // TODO fix "TYPE, ASSEMBLYNAME, CULTURE" pattern
            // TODO fix "TYPE, ASSEMBLYNAME, VERSION, CULTURE, TOKEN" pattern
            var match = TYPE_RE.Match(content);
            if (match.Success)
            {
                string type = match.Groups[1].Value;
                // TODO handle renamed types
                var td = TargetAssemblyMainModule.GetType(type);
                if (td != null && MergedAssemblies.Any(x => x.Name.Name == match.Groups[2].Value))
                {
                    // TODO no public key token !
                    return td.FullName + ", " + TargetAssemblyDefinition.FullName;
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
                    WARN("[" + type + "] attribute wasn't merged because of inconsistency accross merged assemblies");
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
            if (param.HasConstant)
              pd.Constant = param.Constant;
            if (param.HasMarshalInfo)
                pd.MarshalInfo = param.MarshalInfo;
            if (param.HasCustomAttributes)
                CopyCustomAttributes(param.CustomAttributes, pd.CustomAttributes, context);
            col.Add(pd);
        }

        private CustomAttributeArgument Copy(CustomAttributeArgument arg, IGenericParameterProvider context)
        {
            return new CustomAttributeArgument(Import(arg.Type, context), ImportCustomAttributeValue(arg.Value, context));
        }

        private object ImportCustomAttributeValue(object obj, IGenericParameterProvider context)
        {
            if (obj is TypeReference)
                return Import((TypeReference)obj, context);
            if (obj is CustomAttributeArgument)
                return Copy((CustomAttributeArgument)obj, context);
            if (obj is CustomAttributeArgument[])
                return ((CustomAttributeArgument[])obj).Select(a => Copy(a, context)).ToArray();
            return obj;
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
                ed.AddMethod = FindMethodInNewType(nt, evt.AddMethod);
            if (evt.RemoveMethod != null)
                ed.RemoveMethod = FindMethodInNewType(nt, evt.RemoveMethod);
            if (evt.InvokeMethod != null)
                ed.InvokeMethod = FindMethodInNewType(nt, evt.InvokeMethod);
            if (evt.HasOtherMethods)
            {
                foreach (MethodDefinition meth in evt.OtherMethods)
                {
                    var nm = FindMethodInNewType(nt, meth);
                    if (nm != null)
                        ed.OtherMethods.Add(nm);
                }
            }

            CopyCustomAttributes(evt.CustomAttributes, ed.CustomAttributes, nt);
        }

        private MethodDefinition FindMethodInNewType(TypeDefinition nt, MethodDefinition methodDefinition)
        {
            var ret = reflectionHelper.FindMethodDefinitionInType(nt, methodDefinition);
            if (ret == null)
            {
                WARN("Method '" + methodDefinition.FullName + "' not found in merged type '" + nt.FullName + "'");
            }
            return ret;
        }

        private void CopyCustomAttributes(Collection<CustomAttribute> input, Collection<CustomAttribute> output, IGenericParameterProvider context)
        {
            CopyCustomAttributes(input, output, true, context);
        }

        private CustomAttribute Copy(CustomAttribute ca, IGenericParameterProvider context)
        {
            CustomAttribute newCa = new CustomAttribute(Import(ca.Constructor));
            foreach (var arg in ca.ConstructorArguments)
                newCa.ConstructorArguments.Add(Copy(arg, context));
            foreach (var arg in ca.Fields)
                newCa.Fields.Add(Copy(arg, context));
            foreach (var arg in ca.Properties)
                newCa.Properties.Add(Copy(arg, context));
            return newCa;
        }

        private void CopyCustomAttributes(Collection<CustomAttribute> input, Collection<CustomAttribute> output, bool allowMultiple, IGenericParameterProvider context)
        {
            foreach (CustomAttribute ca in input)
            {
                var caType = ca.AttributeType;
                var similarAttributes = output.Where(attr => reflectionHelper.AreSame(attr.AttributeType, caType)).ToList();
                if (similarAttributes.Count != 0)
                {
                    if (!allowMultiple)
                        continue;
                    if (!CustomAttributeTypeAllowsMultiple(caType))
                        continue;
                    if (similarAttributes.Any(x =>
                            reflectionHelper.AreSame(x.ConstructorArguments, ca.ConstructorArguments) &&
                            reflectionHelper.AreSame(x.Fields, ca.Fields) &&
                            reflectionHelper.AreSame(x.Properties, ca.Properties)
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

        private void CopyTypeReferences(Collection<TypeReference> input, Collection<TypeReference> output, IGenericParameterProvider context)
        {
            foreach (TypeReference ta in input)
            {
                output.Add(Import(ta, context));
            }
        }

        public TypeDefinition GetMergedTypeFromTypeRef(TypeReference reference)
        {
            return mappingHandler.GetRemappedType(reference);
        }

        private TypeReference Import(TypeReference reference, IGenericParameterProvider context)
        {
            TypeDefinition type = GetMergedTypeFromTypeRef(reference);
            if (type != null)
                return type;

            reference = platformFixer.FixPlatformVersion(reference);

            if (context == null)
            {
                // we come here when importing types used for assembly-level custom attributes
                return TargetAssemblyMainModule.Import(reference);
            }
            return TargetAssemblyMainModule.Import(reference, context);
        }

        private FieldReference Import(FieldReference reference, IGenericParameterProvider context)
        {
            FieldReference importReference = platformFixer.FixPlatformVersion(reference);

            return TargetAssemblyMainModule.Import(importReference, context);
        }

        private MethodReference Import(MethodReference reference)
        {
            MethodReference importReference = platformFixer.FixPlatformVersion(reference);
            return TargetAssemblyMainModule.Import(importReference);
        }

        private MethodReference Import(MethodReference reference, IGenericParameterProvider context)
        {
            // If this is a Method/TypeDefinition, it will be corrected to a definition again later

            MethodReference importReference = platformFixer.FixPlatformVersion(reference);

            return TargetAssemblyMainModule.Import(importReference, context);

        }

        private void CloneTo(PropertyDefinition prop, TypeDefinition nt, Collection<PropertyDefinition> col)
        {
            // ignore duplicate property
            var others = nt.Properties.Where(x => x.Name == prop.Name).ToList();
            if (others.Any())
            {
                bool skip = false;
                if (!IsIndexer(prop) || !IsIndexer(others.First()))
                {
                    skip = true;
                }
                else
                {
                    // "Item" property is used to implement Indexer operators
                    // It may be specified more than one, with extra arguments to get/set methods
                    // Note than one may also define a standard "Item" property, in which case he won't be able to define Indexers
                    
                    // Here we try to prevent duplicate indexers, but allow to merge non-duplicated ones (e.g. this[int] & this[string] )
                    var args = ExtractIndexerParameters(prop);
                    if (others.Any(x => reflectionHelper.AreSame(args, ExtractIndexerParameters(x))))
                    {
                        skip = true;
                    }
                }
                if (skip)
                {
                    IGNOREDUP("property", prop);
                    return;
                }
            }

            PropertyDefinition pd = new PropertyDefinition(prop.Name, prop.Attributes, Import(prop.PropertyType, nt));
            col.Add(pd);
            if (prop.SetMethod != null)
                pd.SetMethod = FindMethodInNewType(nt, prop.SetMethod);
            if (prop.GetMethod != null)
                pd.GetMethod = FindMethodInNewType(nt, prop.GetMethod);
            if (prop.HasOtherMethods)
            {
                foreach (MethodDefinition meth in prop.OtherMethods)
                {
                    var nm = FindMethodInNewType(nt, meth);
                    if (nm != null)
                        pd.OtherMethods.Add(nm);
                }
            }

            CopyCustomAttributes(prop.CustomAttributes, pd.CustomAttributes, nt);
        }

        private static IList<ParameterDefinition> ExtractIndexerParameters(PropertyDefinition prop)
        {
            if (prop.GetMethod != null)
                return prop.GetMethod.Parameters;
            if (prop.SetMethod != null)
                return prop.SetMethod.Parameters.ToList().GetRange(0, prop.SetMethod.Parameters.Count-1);
            return null;
        }

        private static bool IsIndexer(PropertyDefinition prop)
        {
            if (prop.Name != "Item")
                return false;
            var parameters = ExtractIndexerParameters(prop);
            return parameters != null && parameters.Count > 0;
        }

        private void CloneTo(MethodDefinition meth, TypeDefinition type, bool typeJustCreated)
        {
            // ignore duplicate method for merged duplicated types
            if (!typeJustCreated && 
                type.Methods.Count > 0 &&
                type.Methods.Any(x => 
                  (x.Name == meth.Name) && 
                  (x.Parameters.Count == meth.Parameters.Count) &&
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
                if (meth.PInvokeInfo == null)
                {
                    // Even if this was allowed, I'm not sure it'd work out
                    //nm.RVA = meth.RVA;
                }
                else
                {
                    nm.PInvokeInfo = new PInvokeInfo(meth.PInvokeInfo.Attributes, meth.PInvokeInfo.EntryPoint, meth.PInvokeInfo.Module);
                }
            }

            foreach (ParameterDefinition param in meth.Parameters)
                CloneTo(param, nm, nm.Parameters);

            foreach (MethodReference ov in meth.Overrides)
                nm.Overrides.Add(Import(ov, nm));

            CopySecurityDeclarations(meth.SecurityDeclarations, nm.SecurityDeclarations, nm);
            CopyCustomAttributes(meth.CustomAttributes, nm.CustomAttributes, nm);

            nm.ReturnType = Import(meth.ReturnType, nm);
            CopyCustomAttributes(meth.MethodReturnType.CustomAttributes, nm.MethodReturnType.CustomAttributes, nm);

            if (meth.HasBody)
                CloneTo(meth.Body, nm);
            meth.Body = null; // frees memory

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
                nb.Variables.Add(new VariableDefinition(var.Name, 
                    Import(var.VariableType, parent)));

            nb.Instructions.SetCapacity(body.Instructions.Count);
            LineIndexer.PreMethodBodyRepack(body, parent);
            foreach (Instruction instr in body.Instructions)
            {
                LineIndexer.ProcessMethodBodyInstruction(instr);

                Instruction ni;

                if (instr.OpCode.Code == Code.Calli)
                {
                    var call_site = (Mono.Cecil.CallSite)instr.Operand;
                    Mono.Cecil.CallSite ncs = new CallSite(Import(call_site.ReturnType, parent)) {
                        HasThis = call_site.HasThis,
                        ExplicitThis = call_site.ExplicitThis,
                        CallingConvention = call_site.CallingConvention
                    };
                    ni = Instruction.Create(instr.OpCode, ncs);
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
                        FixAspNetOffset(nb.Instructions, (MethodReference)instr.Operand, parent);
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
            LineIndexer.PostMethodBodyRepack(parent);

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
                        break;
                }

                nb.ExceptionHandlers.Add(neh);
            }
        }

        private void FixAspNetOffset(Collection<Instruction> instructions, MethodReference operand, MethodDefinition parent)
        {
            if (operand.Name == "WriteUTF8ResourceString" || operand.Name == "CreateResourceBasedLiteralControl")
            {
                var fullName = operand.FullName;
                if (fullName == "System.Void System.Web.UI.TemplateControl::WriteUTF8ResourceString(System.Web.UI.HtmlTextWriter,System.Int32,System.Int32,System.Boolean)" ||
                    fullName == "System.Web.UI.LiteralControl System.Web.UI.TemplateControl::CreateResourceBasedLiteralControl(System.Int32,System.Int32,System.Boolean)")
                {
                    int offset;
                    if (aspOffsets.TryGetValue(parent.Module.Assembly, out offset))
                    {
                        int prev = (int)instructions[instructions.Count - 4].Operand;
                        instructions[instructions.Count - 4].Operand = prev + offset;
                    }
                }
            }
        }

        internal static Instruction GetInstruction(MethodBody oldBody, MethodBody newBody, Instruction i)
        {
            int pos = oldBody.Instructions.IndexOf(i);
            if (pos > -1 && pos < newBody.Instructions.Count)
                return newBody.Instructions[pos];

            return null /*newBody.Instructions.Outside*/;
        }

        internal TypeDefinition Import(TypeDefinition type, Collection<TypeDefinition> col, bool internalize)
        {
            TypeDefinition nt = TargetAssemblyMainModule.GetType(type.FullName);
            bool justCreatedType = false;
            if (nt == null)
            {
                nt = CreateType(type, col, internalize, null);
                justCreatedType = true;
            }
            else if (DuplicateTypeAllowed(type))
            {
                INFO("Merging " + type);
            }
            else if (!type.IsPublic || internalize)
            {
                // rename it
                string other = "<" + Guid.NewGuid() + ">" + type.Name;
                INFO("Renaming " + type.FullName + " into " + other);
                nt = CreateType(type, col, internalize, other);
                justCreatedType = true;
            }
            else if (UnionMerge)
            {
                INFO("Merging " + type);
            }
            else
            {
                ERROR("Duplicate type " + type);
                throw new InvalidOperationException("Duplicate type " + type + " from " + type.Scope + ", was also present in " + mappingHandler.GetOrigTypeModule(nt));
            }
            mappingHandler.StoreRemappedType(type, nt);

            // nested types first (are never internalized)
            foreach (TypeDefinition nested in type.NestedTypes)
                Import(nested, nt.NestedTypes, false);
            foreach (FieldDefinition field in type.Fields)
                CloneTo(field, nt);

            // methods before fields / events
            foreach (MethodDefinition meth in type.Methods)
                CloneTo(meth, nt, justCreatedType);

            foreach (EventDefinition evt in type.Events)
                CloneTo(evt, nt, nt.Events);
            foreach (PropertyDefinition prop in type.Properties)
                CloneTo(prop, nt, nt.Properties);
            return nt;
        }

        private TypeDefinition CreateType(TypeDefinition type, Collection<TypeDefinition> col, bool internalize, string rename)
        {
            TypeDefinition nt = new TypeDefinition(type.Namespace, rename ?? type.Name, type.Attributes);
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
            return nt;
        }

        internal TypeReference CreateReference(ExportedType type)
        {
            return new TypeReference(type.Namespace, type.Name, TargetAssemblyMainModule, type.Scope)
            {
                DeclaringType = type.DeclaringType != null ? CreateReference(type.DeclaringType) : null,
            };
        }

        internal void Import(ExportedType type, Collection<ExportedType> col, ModuleDefinition module)
        {
            var nt = new ExportedType(type.Namespace, type.Name, module, type.Scope)
                {
                    Attributes = type.Attributes,
                    Identifier = type.Identifier, // TODO: CHECK THIS when merging multiple assemblies when exported types ?
                    DeclaringType = type.DeclaringType
                };

            // try to skip redirects to merged assemblies
            if (type.Scope is AssemblyNameReference)
            {
                if (MergedAssemblies.Any(x => x.Name.Name == ((AssemblyNameReference)type.Scope).Name))
                {
                    return;
                }
            }
            else if (type.Scope is ModuleReference)
            {
                if (MergedAssemblies.SelectMany(x => x.Modules).Any(x => x.Name == ((ModuleReference)type.Scope).Name))
                {
                    return;
                }
            }
            col.Add(nt);
        }

        public TypeReference GetExportedTypeFromTypeRef(TypeReference type)
        {
            return mappingHandler.GetExportedRemappedType(type) ?? type;
        }

        private bool DuplicateTypeAllowed(TypeDefinition type)
        {
            string fullName = type.FullName;
            // Merging module because IKVM uses this class to store some fields.
            // Doesn't fully work yet, as IKVM is nice enough to give all the fields the same name...
            if (fullName == "<Module>" || fullName == "__<Proxy>")
                return true;

            // XAML helper class, identical in all assemblies, unused within the assembly, and instanciated through reflection from the outside
            // We could just skip them after the first one, but merging them works just fine
            if (fullName == "XamlGeneratedNamespace.GeneratedInternalTypeHelper")
                return true;

            // Merge should be OK since member's names are pretty unique,
            // but renaming duplicate members would be safer...
            if (fullName == "<PrivateImplementationDetails>" && type.IsPublic)
                return true;

            if (allowedDuplicateTypes.Contains(fullName))
                return true;

            var top = type;
            while (top.IsNested)
                top = top.DeclaringType;
            string nameSpace = top.Namespace;
            if (!String.IsNullOrEmpty(nameSpace) && allowedDuplicateNameSpaces.Any(s => s == nameSpace || nameSpace.StartsWith(s + ".")))
                return true;

            return false;
        }
    }
}
