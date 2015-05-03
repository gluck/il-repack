using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ILRepack.Tests.NuGet
{
    public class Package
    {
        public string Name {get; private set;}
        public string Version {get; private set;}
        public Func<string, bool> AssembliesMatcher {get; private set;}

        public Package WithFwk(string fwk)
        {
            var pattern = string.Format(@"lib\{0}\{1}.dll", fwk, this.Name);
            return WithMatcher(file => String.Equals(file, pattern, StringComparison.InvariantCultureIgnoreCase) );
        }

        public Package WithMatcher(Func<string, bool> matcher)
        {
            return new Package { Name = this.Name, Version = this.Version, AssembliesMatcher = matcher };
        }

        public bool Matches<T>(Tuple<string, T> item)
        {
            return AssembliesMatcher(item.Item1);
        }

        public static Package From(string name, string version)
        {
            return new Package { Name = name, Version = version };
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}", Name, Version);
        }

    }
}
