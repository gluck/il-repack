using System.Diagnostics.Contracts;
using System.Text.RegularExpressions;

namespace ILRepacking.Steps.SourceServerData
{
    internal class SourceFileDescriptor
    {
        private static readonly Regex Regex = new Regex(@"([^\*]*)(\*([^\*]*))?");

        public string Path { get; }
        public string Variable2 { get; }

        public SourceFileDescriptor(string path, string variable2)
        {
            Contract.Assert(path != null);
            Contract.Assert(variable2 != null);

            Path = path;
            Variable2 = variable2;
        }

        public static SourceFileDescriptor Parse(string raw)
        {
            raw = raw ?? "";
            var groups = Regex.Match(raw).Groups;
            return new SourceFileDescriptor(groups[1].Value, groups[3].Value);
        }

        public override string ToString()
        {
            return $"{Path}*{Variable2}";
        }
    }
}