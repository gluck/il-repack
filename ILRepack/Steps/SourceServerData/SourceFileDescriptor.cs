using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text.RegularExpressions;

namespace ILRepacking.Steps.SourceServerData
{
    internal class SourceFileDescriptor
    {
        private static readonly Regex Regex = new Regex(@"([^\*]*)(\*([^\*]*))*");

        public string[] Variables { get; }

        public SourceFileDescriptor(string path, string variable2)
        {
            Contract.Assert(path != null);
            Contract.Assert(variable2 != null);

            Variables = new[] { path, variable2 };
        }

        public SourceFileDescriptor(IEnumerable<string> variables)
        {
            Contract.Assert(variables != null);
            Variables = variables.ToArray();
        }

        public static SourceFileDescriptor Parse(string raw)
        {
            var groups = Regex.Match(raw ?? "").Groups;
            return new SourceFileDescriptor(
                    new[] { groups[1].Value }
                    .Concat(groups[3].Captures
                        .Cast<Capture>()
                        .Select(capture => capture.Value)));
        }

        public override string ToString()
        {
            return string.Join("*", Variables);
        }
    }
}