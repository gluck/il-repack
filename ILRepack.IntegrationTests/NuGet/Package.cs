using System;
using System.IO;

namespace ILRepack.IntegrationTests.NuGet
{
    public class Package
    {
        public string Name {get; private set;}
        public string Version {get; private set;}
        public Func<string, bool> AssembliesMatcher {get; private set;}

        public Package WithFwk(string fwk)
        {
            return WithArtifact($"lib{Path.DirectorySeparatorChar}{fwk}{Path.DirectorySeparatorChar}{this.Name}.dll");
        }

        public Package WithArtifact(string artifact)
        {
            return WithMatcher(file => String.Equals(file, artifact.Replace('/', Path.DirectorySeparatorChar), StringComparison.InvariantCultureIgnoreCase));
        }

        public Package WithMatcher(Func<string, bool> matcher)
        {
            return new Package { Name = this.Name, Version = this.Version, AssembliesMatcher = matcher };
        }

        public bool Matches(string text)
        {
            if (AssembliesMatcher == null)
            {
                return true;
            }

            return AssembliesMatcher(text);
        }

        public static Package From(string name, string version)
        {
            return new Package { Name = name, Version = version };
        }

        public override string ToString()
        {
            return $"{Name}:{Version}";
        }
    }
}
