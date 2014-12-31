
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
using System.Text.RegularExpressions;
using System.Threading;
using Mono.Cecil;

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


    public partial class ILRepack
    {
        // keep ILMerge syntax (both command-line & api) for compatibility (commented out: not implemented yet)

        public void AllowDuplicateType(string typeName)
        {
            if (typeName.EndsWith(".*"))
            {
                allowedDuplicateNameSpaces.Add(typeName.Substring(0, typeName.Length - 2));
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

        internal List<string> MergedAssemblyFiles { get; set; }
        internal string PrimaryAssemblyFile { get; set; }
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
                    if (PrimaryAssemblyDefinition == null)
                    {
                        PrimaryAssemblyDefinition = mergeAsm;
                        PrimaryAssemblyFile = assembly;
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
            if (!AllowWildCards || s.IndexOfAny(new[] { '*', '?' }) == -1)
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
    }
}
