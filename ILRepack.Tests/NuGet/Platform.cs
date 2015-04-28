using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ILRepack.Tests.NuGet
{
    public class Platform
    {
        public IEnumerable<Package> Packages;

        public static Platform From(IEnumerable<Package> packages)
        {
            return new Platform() { Packages = packages };
        }

        public IEnumerable<Platform> WithFwks(IEnumerable<string> fwks)
        {
            return fwks.Select(fwk => WithFwk(fwk));
        }

        public Platform WithFwk(string fwk)
        {
            return From(Packages.Select(p => p.WithFwk(fwk)));
        }

        public override string ToString()
        {
            return string.Join(",", Packages.Select(p => string.Format("{0}:{1}@{2}", p.Name, p.Version, string.Join("/", p.Assemblies))));
        }
    }
}
