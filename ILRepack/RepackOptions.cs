using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ILRepacking
{
    public class RepackOptions
    {
        internal class InvalidTargetKindException : Exception
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

        /// <summary>
        /// Gets or sets a file that contains one regex per line to compare against 
        /// FullName of types NOT to internalize. The items will replace the contents of
        /// <see cref="ExcludeInternalizeMatches" />. This option only has an effect if
        /// <see cref="Internalize"/> is set to true. 
        /// </summary>
        public string ExcludeFile
        {
            get { return excludeFile; }
            set
            {
                excludeFile = value;
                ExcludeInternalizeMatches.Clear();
                if (!string.IsNullOrEmpty(excludeFile))
                {
                    string[] lines = file.ReadAllLines(excludeFile);
                    foreach (var line in lines)
                        ExcludeInternalizeMatches.Add(new Regex(line));
                }
            }
        }

        public int FileAlignment { get; set; } // UNIMPL, not supported by cecil
        public string[] InputAssemblies { get; set; }
        public bool Internalize { get; set; }
        public string KeyFile { get; set; }
        public string KeyContainer { get; set; }
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
        public IEnumerable<string> SearchDirectories { get; set; }
        public bool UnionMerge { get; set; }
        public Version Version { get; set; }
        public bool XmlDocumentation { get; set; }

        // end of ILMerge-similar attributes

        public bool LogVerbose { get; set; }
        public bool NoRepackRes { get; set; }
        public bool KeepOtherVersionReferences { get; set; }
        public bool LineIndexation { get; set; }

        /// <summary>
        /// If Internalize is set to true, any which match these 
        /// regular expressions will not be internalized. 
        /// If internalize is false, then this property is ignored.
        /// </summary>
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

        public string RepackDropAttribute { get; set; }
        public bool RenameInternalized { get; set; }

        private readonly Hashtable allowedDuplicateTypes = new Hashtable();
        private readonly List<string> allowedDuplicateNameSpaces = new List<string>();
        private readonly List<Regex> excludeInternalizeMatches = new List<Regex>();
        private readonly ICommandLine cmd;
        private readonly IFile file;
        private string excludeFile;

        private void AllowDuplicateType(string typeName)
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

        public RepackOptions()
            : this(new CommandLine(Enumerable.Empty<String>()))
        {
        }

        public RepackOptions(IEnumerable<string> ilRepackArguments)
            : this(new CommandLine(ilRepackArguments))
        {
        }

        public RepackOptions(CommandLine commandLine)
            : this(commandLine, new FileWrapper())
        {
        }

        internal RepackOptions(ICommandLine commandLine, IFile file)
        {
            cmd = commandLine;
            this.file = file;
            if (!ShouldShowUsage)
                Parse();
        }

        internal bool ShouldShowUsage => cmd.Modifier("?") || cmd.Modifier("help") || cmd.Modifier("h") || cmd.HasNoOptions;

        void Parse()
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

            RenameInternalized = cmd.Modifier("renameinternalized");
            KeyFile = cmd.Option("keyfile");
            KeyContainer = cmd.Option("keycontainer");
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

            SearchDirectories = cmd.Options("lib");

            // private cmdline-Options:
            LogVerbose = cmd.Modifier("verbose");
            LineIndexation = cmd.Modifier("index");

            if (cmd.HasOption("repackdrop"))
            {
                RepackDropAttribute = cmd.Option("repackdrop");
                if (String.IsNullOrWhiteSpace(RepackDropAttribute))
                {
                    RepackDropAttribute = "RepackDropAttribute";
                }
            }

            // everything that doesn't start with a '/' must be a file to merge (verify when loading the files)
            InputAssemblies = cmd.OtherAguments;
        }

        /// <summary>
        /// Validates the options for repack execution, throws upon invalid argument set
        /// </summary>
        internal void Validate()
        {
            if (DelaySign && KeyFile == null && KeyContainer == null)
                throw new InvalidOperationException("Option 'delaysign' is only valid with 'keyfile' or 'keycontainer'.");

            if (AllowMultipleAssemblyLevelAttributes && !CopyAttributes)
                throw new InvalidOperationException("Option 'allowMultiple' is only valid with 'copyattrs'.");

            if (!string.IsNullOrEmpty(AttributeFile) && CopyAttributes)
                throw new InvalidOperationException("Option 'attr' can not be used with 'copyattrs'.");

            if (RenameInternalized && !Internalize)
                throw new InvalidOperationException("Option 'renameInternalized' is only valid with 'internalize'.");
            
            if (string.IsNullOrEmpty(OutputFile))
                throw new ArgumentException("No output file given.");

            if (InputAssemblies == null || InputAssemblies.Length == 0)
                throw new ArgumentException("No input files given.");

            if ((KeyFile != null) && !file.Exists(KeyFile))
                throw new ArgumentException($"KeyFile does not exist: '{KeyFile}'.");
        }

        public IList<string> ResolveFiles()
        {
            return InputAssemblies.SelectMany(ResolveFile).Distinct().ToList();
        }

        IEnumerable<string> ResolveFile(string s)
        {
            if (!AllowWildCards || s.IndexOfAny(new[] { '*', '?' }) == -1)
                return new[] { s };
            if (Path.GetDirectoryName(s).IndexOfAny(new[] { '*', '?' }) != -1)
                throw new Exception("Invalid path: " + s);
            string dir = Path.GetDirectoryName(s);
            if (String.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            return Directory.GetFiles(Path.GetFullPath(dir), Path.GetFileName(s));
        }

        public string ToCommandLine()
        {
            StringBuilder commandLine = new StringBuilder();

            var assembliesArgument = InputAssemblies.Aggregate(
                string.Empty,
                (previous, item) => previous + ' ' + item);

            commandLine.AppendLine("------------- IL Repack Arguments -------------");
            commandLine.Append($"/out:{OutputFile} ");
            commandLine.Append(!string.IsNullOrEmpty(KeyFile) ? $"/keyfile:{KeyFile} " : string.Empty);
            commandLine.Append(Internalize ? "/internalize" : string.Empty);
            commandLine.AppendLine(assembliesArgument);
            commandLine.Append("-----------------------------------------------");

            return commandLine.ToString();
        }
    }
}
