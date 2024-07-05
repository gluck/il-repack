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
                ExcludeInternalizeAssemblies.Clear();
                if (!string.IsNullOrEmpty(excludeFile))
                {
                    string[] lines = file.ReadAllLines(excludeFile);
                    foreach (var line in lines)
                    {
                        ExcludeInternalizeMatches.Add(new Regex(line));
                        ExcludeInternalizeAssemblies.Add(StripExtension(line));
                    }
                }
            }
        }

        public int FileAlignment { get; set; } // UNIMPL, not supported by cecil
        public string[] InputAssemblies { get; set; }
        public bool Internalize { get; set; }
        public bool ExcludeInternalizeSerializable { get; set; }
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
        public IEnumerable<string> SearchDirectories { get; set; } = Array.Empty<string>();
        public bool UnionMerge { get; set; }
        public Version Version { get; set; }
        public bool SkipConfigMerge { get; set; }
        public bool MergeIlLinkerFiles { get; set; }
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
        public HashSet<string> ExcludeInternalizeAssemblies
        {
            get { return excludeInternalizeAssemblies; }
        }

        public IReadOnlyList<string> InternalizeAssemblies { get; set; } = Array.Empty<string>();

        public Hashtable AllowedDuplicateTypes
        {
            get { return allowedDuplicateTypes; }
        }

        public List<string> AllowedDuplicateNameSpaces
        {
            get { return allowedDuplicateNameSpaces; }
        }

        public bool AllowAllDuplicateTypes { get; set; }

        public string RepackDropAttribute { get; set; }
        public HashSet<string> RepackDropAttributes { get; } = new HashSet<string>();

        public bool RenameInternalized { get; set; }

        private readonly Hashtable allowedDuplicateTypes = new Hashtable();
        private readonly List<string> allowedDuplicateNameSpaces = new List<string>();
        private readonly List<Regex> excludeInternalizeMatches = new List<Regex>();
        private readonly HashSet<string> excludeInternalizeAssemblies = new HashSet<string>();
        private readonly ICommandLine cmd;
        private readonly IFile file;
        private string excludeFile;

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
            ShouldShowUsage = cmd.Modifier("?") || cmd.Modifier("help") || cmd.Modifier("h") || cmd.HasNoOptions;
            PauseBeforeExit = cmd.Modifier("pause");
            if (!ShouldShowUsage)
            {
                Parse();
            }
        }

        internal bool ShouldShowUsage { get; private set; }

        void Parse()
        {
            AllowDuplicateResources = cmd.Modifier("allowduplicateresources");

            bool allowDupModifier = cmd.Modifier("allowdup");

            foreach (string dupType in cmd.Options("allowdup"))
            {
                if (!string.IsNullOrEmpty(dupType))
                {
                    AllowDuplicateType(dupType);
                }
            }

            if (allowedDuplicateTypes.Count == 0 && allowDupModifier)
            {
                AllowAllDuplicateTypes = true;
            }

            AllowMultipleAssemblyLevelAttributes = cmd.Modifier("allowmultiple");
            AllowWildCards = cmd.Modifier("wildcards");
            AllowZeroPeKind = cmd.Modifier("zeropekind");
            Parallel = cmd.Modifier("parallel");
            AttributeFile = cmd.Option("attr");
            Closed = cmd.Modifier("closed");
            CopyAttributes = cmd.Modifier("copyattrs");
            DebugInfo = !cmd.Modifier("ndebug");
            DelaySign = cmd.Modifier("delaysign");
            cmd.Option("align"); // not supported, just prevent interpreting this as file...

            Internalize = cmd.HasOption("internalize");
            if (Internalize)
            {
                // this file shall contain one regex per line to compare against FullName of types NOT to internalize
                ExcludeFile = cmd.Option("internalize");
            }

            RenameInternalized = cmd.Modifier("renameinternalized");
            ExcludeInternalizeSerializable = cmd.Modifier("excludeinternalizeserializable");
            InternalizeAssemblies = cmd.Options("internalizeassembly").Select(StripExtension).ToArray();

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
            SkipConfigMerge = cmd.Modifier("skipconfig");
            MergeIlLinkerFiles = cmd.Modifier("illink");
            XmlDocumentation = cmd.Modifier("xmldocs");
            NoRepackRes = cmd.Modifier("norepackres");
            KeepOtherVersionReferences = cmd.Modifier("keepotherversionreferences");

            SearchDirectories = cmd
                .Options("lib")
                .Concat(new[] { Environment.CurrentDirectory })
                .Select(d => Path.GetFullPath(d))
                .Distinct()
                .ToArray();

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

                // Disambiguate overload for .net8 between string? and [char].
                RepackDropAttributes.UnionWith(RepackDropAttribute.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            }

            // everything that doesn't start with a '/' must be a file to merge (verify when loading the files)
            InputAssemblies = cmd.OtherArguments;
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

            if (!string.IsNullOrEmpty(OutputFile))
            {
                try
                {
                    OutputFile = Path.GetFullPath(OutputFile);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Output file {OutputFile} is not valid: {ex.Message}");
                }
            }
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

        private static string StripExtension(string filePath)
        {
            if (filePath == null)
            {
                return null;
            }

            if (filePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                filePath = filePath.Substring(0, filePath.Length - 4);
            }

            return filePath;
        }

        public string ToCommandLine()
        {
            var commandLine = new StringBuilder();
            commandLine.AppendLine("------------- IL Repack Arguments -------------");
            commandLine.AppendLine(cmd.ToString());
            commandLine.Append("-----------------------------------------------");
            return commandLine.ToString();
        }
    }
}
