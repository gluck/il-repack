﻿using System.Collections.Generic;
using System.Linq;

namespace ILRepack.Tests.NuGet
{
    public class Platform
    {
        public IEnumerable<Package> Packages { get; private set; }
        public IEnumerable<string> Args { get; private set; }

        public Platform()
        {
            Packages = Enumerable.Empty<Package>();
            Args = Enumerable.Empty<string>();
        }

        public static Platform From(IEnumerable<Package> packages)
        {
            return new Platform() { Packages = packages };
        }

        public static Platform From(params Package[] packages)
        {
            return From((IEnumerable<Package>)packages);
        }

        public IEnumerable<Platform> WithFwks(IEnumerable<string> fwks)
        {
            return fwks.Select(fwk => WithFwk(fwk));
        }

        public IEnumerable<Platform> WithFwks(params string[] fwks)
        {
            return WithFwks((IEnumerable<string>)fwks);
        }

        public Platform WithFwk(string fwk)
        {
            return From(Packages.Select(p => p.WithFwk(fwk)));
        }

        public Platform WithExtraArgs(params string[] args)
        {
            return new Platform { Packages = this.Packages, Args = this.Args.Concat(args) };
        }

        public override string ToString()
        {
            return string.Join(",", Packages);
        }
    }
}
