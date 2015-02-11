using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ILRepacking
{
    class RepackOptions
    {
        public class InvalidTargetKindException : Exception 
        {
            public InvalidTargetKindException(string message) : base(message) 
            { 
            }
        }

        // keep ILMerge syntax (both command-line & api) for compatibility (commented out: not implemented yet)
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

        public string OutputFile { get; set; }
        public bool PublicKeyTokens { get; set; } // UNIMPL

        public bool StrongNameLost { get; set; }
        public ILRepack.Kind? TargetKind { get; set; }
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
        public RepackAssemblyResolver GlobalAssemblyResolver 
        { 
            get { return globalAssemblyResolver; } 
        }
        public List<Regex> ExcludeInternalizeMatches
        {
            get { return excludeInternalizeMatches; }
        }
        public Hashtable AllowedDuplicateTypes
        {
            get { return allowedDuplicateTypes; }
        }
        public List<string> AllowedDuplicateNameSpaces
        {
            get { return allowedDuplicateNameSpaces; }
        }
        
        private readonly Hashtable allowedDuplicateTypes = new Hashtable();
        private readonly List<string> allowedDuplicateNameSpaces = new List<string>();
        private readonly ICommandLine cmd;
        private readonly ILogger logger;
        private readonly IFile file;
        private readonly RepackAssemblyResolver globalAssemblyResolver = new RepackAssemblyResolver();
        private List<Regex> excludeInternalizeMatches;

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

        public RepackOptions(ICommandLine commandLine, ILogger logger, IFile file)
        {
            this.cmd = commandLine;
            this.logger = logger;
            this.file = file;
        }

        public bool ShouldShowUsage()
        {
            return cmd.Modifier("?") | cmd.Modifier("help") | cmd.Modifier("h") | cmd.HasNoOptions;
        }

        public void Parse()
        {
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
                        TargetKind = ILRepack.Kind.Dll;
                        break;
                    case ("exe"):
                        TargetKind = ILRepack.Kind.Exe;
                        break;
                    case ("winexe"):
                        TargetKind = ILRepack.Kind.WinExe;
                        break;
                    default:
                        throw new InvalidTargetKindException("Invalid target: \"" + targetKind + "\".");
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

            // private cmdline-Options:
            LogVerbose = cmd.Modifier("verbose");
            LineIndexation = cmd.Modifier("index");

            if (string.IsNullOrEmpty(KeyFile) && DelaySign)
                logger.WARN("Option 'delaysign' is only valid with 'keyfile'.");
            if (AllowMultipleAssemblyLevelAttributes && !CopyAttributes)
                logger.WARN("Option 'allowMultiple' is only valid with 'copyattrs'.");
            if (!string.IsNullOrEmpty(AttributeFile) && (CopyAttributes))
                logger.WARN("Option 'attr' can not be used with 'copyattrs'.");

            // everything that doesn't start with a '/' must be a file to merge (verify when loading the files)
            InputAssemblies = cmd.OtherAguments;
        }

        /// <summary>
        /// Parse contents of properties: central point for checking (set on assembly or through command-line).
        /// </summary>
        public void ParseProperties()
        {
            if (string.IsNullOrEmpty(OutputFile))
            {
                throw new ArgumentException("No output file given.");
            }

            if ((InputAssemblies == null) || (InputAssemblies.Length == 0))
            {
                throw new ArgumentException("No input files given.");
            }

            if ((KeyFile != null) && !file.Exists(KeyFile))
            {
                throw new ArgumentException("KeyFile does not exist: \"" + KeyFile + "\".");
            }

            if (Internalize && !string.IsNullOrEmpty(ExcludeFile))
            {
                string[] lines = file.ReadAllLines(ExcludeFile);
                excludeInternalizeMatches = new List<Regex>(lines.Length);
                foreach (string line in lines)
                    excludeInternalizeMatches.Add(new Regex(line));
            }
        }

    }
}
