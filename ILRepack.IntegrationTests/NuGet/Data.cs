using ILRepack.IntegrationTests.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ILRepack.IntegrationTests.NuGet
{
    public static class Data
    {
        private static string[] supportedFwks = { @"lib", @"lib/20", @"lib/net20", @"lib/net35", @"lib/net40", @"lib/net4", @"lib/net45" };
        private static readonly IEnumerable<Package> Packages_Win = new[] {
            Package.From("MahApps.Metro", "1.1.2"),
            // Bcl.Async references 4.5, so it only repacks on Windows when 4.5 is installed
            Package.From("Microsoft.Bcl.Async", "1.0.168"),
        };
        private static readonly IEnumerable<Package> Packages_Mono = new Package[0];
        public static readonly IEnumerable<Package> Packages = new[] {
            Package.From("Antlr", "3.5.0.2"),
            Package.From("Autofac", "3.5.2"),
            Package.From("AutoMapper", "3.3.1"),
            Package.From("Castle.Core", "3.3.3"),
            Package.From("Dapper", "1.40.0"),
            Package.From("FSharp.Core", "3.1.2.1"),
            Package.From("Iesi.Collections", "4.0.1.4000"),
            Package.From("Microsoft.Bcl", "1.1.10"),
            Package.From("Microsoft.TeamFoundation.Common", "12.0.21005.1"),
            Package.From("Newtonsoft.Json", "6.0.8"),
            // Cf #124, NHibernate roundtrip no longer pass the PEVerify test
            //Package.From("NHibernate", "4.0.3.4000"),
            Package.From("Ninject", "3.2.2"),
            Package.From("RestSharp", "105.0.1"),
            Package.From("Rx-Core", "2.2.5"),
            Package.From("Rx_Experimental-Main", "1.1.11111"),
            Package.From("SharpZipLib", "0.86.0"),
            Package.From("System.Spatial", "5.6.4"),
        }
        .Concat(XPlat.IsWindows ? Packages_Win : Packages_Mono)
        .Select(p => p.WithMatcher(file => supportedFwks.Select(d => d.Replace('/', Path.DirectorySeparatorChar)).Contains(Path.GetDirectoryName(file).ToLower())));

        public static readonly Package Ikvm = Package.From("IKVM", "8.0.5449.1")
            .WithMatcher(file => string.Equals("lib", Path.GetDirectoryName(file), StringComparison.InvariantCultureIgnoreCase));

        public static readonly IEnumerable<Platform> Platforms = Platform.From(
            Package.From("UnionArgParser", "0.8.7"),
            Package.From("FSharp.Core", "3.0.2")
        ).WithFwks("net35", "net40").Concat(Platform.From(
            Package.From("MassTransit", "2.9.9"),
            Package.From("Magnum", "2.1.3"),
            Package.From("Newtonsoft.Json", "6.0.8")
        ).WithFwks("net35", "net40")).Concat(new [] {
            Platform.From(Ikvm),
            Platform.From(
                Package.From("Paket.Core", "1.11.6").WithFwk("net45"),
                Package.From("FSharp.Core", "3.1.2.1").WithFwk("net40")
            ),
            Platform.From(
                Package.From("FSharp.Compiler.Service", "1.4.0.6").WithFwk("net40"),
                Package.From("FSharp.Core", "4.0.0.1").WithFwk("net40")
            )
        });
    }
}
