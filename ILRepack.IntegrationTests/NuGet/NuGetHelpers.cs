using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace ILRepack.IntegrationTests.NuGet
{
    static class NuGetHelpers
    {
        static NuGetHelpers()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        }

        static HttpClient Http = new HttpClient();

        private static byte[] DownloadBytes(Uri uri)
        {
            return Http.GetByteArrayAsync(uri).Result;
        }

        public static IEnumerable<(string name, Stream stream)> GetNupkgAssembliesAsync(Package package, Predicate<string> fileFilter = null)
        {
            var predicate = fileFilter;
            if (predicate == null)
            {
                predicate = n => package.Matches(n) &&
                    (n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            }

            return GetNupkgContentAsync(package, predicate);
        }
 
        public static IEnumerable<(string name, Stream stream)> GetNupkgContentAsync(Package package, Predicate<string> fileFilter = null)
        {
            var bytes = DownloadBytes(new Uri($"https://www.nuget.org/api/v2/package/{package.Name}/{package.Version}"));
            var stream = new MemoryStream(bytes);
            var zipFile = new ZipFile(stream) { IsStreamOwner = true };
            var entries = zipFile
                .OfType<ZipEntry>()
                .Select(entry => (name: NormalizeEntryName(entry.Name), stream: zipFile.GetInputStream(entry)))
                .Where(entry => fileFilter == null || fileFilter(entry.name))
                .ToArray();
            return entries;
        }

        private static string NormalizeEntryName(string name)
        {
            return name
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace("%2B", "+");
        }
    }
}
