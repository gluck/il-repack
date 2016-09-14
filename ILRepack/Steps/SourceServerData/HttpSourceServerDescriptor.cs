using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ILRepacking.Steps.SourceServerData
{
    internal class HttpSourceServerDescriptor
    {
        private const string InitSection = "SRCSRV: ini ------------------------------------------------";

        private const string VariablesSection = "SRCSRV: variables ------------------------------------------";

        private const string SourceFilesSection = "SRCSRV: source files ---------------------------------------";

        private const string EndSection = "SRCSRV: end ------------------------------------------------";

        private const string VersionKey = "VERSION";

        private const string VersionControlKey = "SRCSRVVERCTRL";

        private const string TargetKey = "SRCSRVTRG";

        private static readonly Regex VariablesRegex = new Regex("(.*)=(.*)");

        public int Version { get; }

        public string VersionControl { get; }

        public string Target { get; }

        public SourceFileDescriptor[] SourceFiles { get; }

        public HttpSourceServerDescriptor(int version, string versionControl, string target, SourceFileDescriptor[] sourceFiles)
        {
            Contract.Assert(versionControl != null);
            Contract.Assert(target != null);
            Contract.Assert(sourceFiles != null);

            Version = version;
            VersionControl = versionControl;
            Target = target;
            SourceFiles = sourceFiles;
        }

        public static bool TryParse(byte[] rawSrcSrv, out HttpSourceServerDescriptor descriptor)
        {
            string currentSection = "";
            int version = 0;
            string versionControl = "";
            string target = "";
            var sources = new List<SourceFileDescriptor>();

            var lines = Regex.Split(Encoding.UTF8.GetString(rawSrcSrv), "\r\n|\r|\n");
            foreach (var line in lines)
            {
                if (new[] { InitSection, VariablesSection, SourceFilesSection, EndSection }.Contains(line))
                {
                    currentSection = line;
                }
                else
                {
                    switch (currentSection)
                    {
                        case InitSection:
                        case VariablesSection:
                            var groups = VariablesRegex.Match(line).Groups;
                            var key = groups[1].Value;
                            var value = groups[2].Value;
                            switch (key)
                            {
                                case VersionKey:
                                    version = int.Parse(value);
                                    break;
                                case VersionControlKey:
                                    versionControl = value;
                                    break;
                                case TargetKey:
                                    target = value;
                                    break;
                            }
                            break;
                        case SourceFilesSection:
                            sources.Add(SourceFileDescriptor.Parse(line));
                            break;
                    }
                }
            }

            descriptor = new[] { "http", "https" }.Contains(versionControl)
                ? new HttpSourceServerDescriptor(version, versionControl, target, sources.ToArray())
                : null;
            return descriptor != null;
        }



        public override string ToString()
        {
            return string.Join(Environment.NewLine, GetRawLines());
        }

        private IEnumerable<string> GetRawLines()
        {
            yield return InitSection;
            yield return $"{VersionKey}={Version}";
            yield return VariablesSection;
            yield return $"{VersionControlKey}={VersionControl}";
            yield return $"{TargetKey}={Target}";
            yield return SourceFilesSection;
            foreach (var sourceFileDescriptor in SourceFiles)
            {
                yield return sourceFileDescriptor.ToString();
            }
            yield return EndSection;
        }
    }
}