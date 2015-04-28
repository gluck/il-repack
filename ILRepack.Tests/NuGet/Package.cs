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
        public IEnumerable<string> Assemblies {get; private set;}

        public Package WithFwk(string fwk)
        {
            return new Package() { Name = this.Name, Version = this.Version, Assemblies = new[] { string.Format(@"lib\{0}\{1}.dll", fwk, this.Name) } };
        }

        public static Package From(string name, string version)
        {
            return new Package() { Name = name, Version = version };
        }
    }
}
