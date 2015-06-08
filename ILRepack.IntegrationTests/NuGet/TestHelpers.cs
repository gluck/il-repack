using ILRepacking;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ILRepack.IntegrationTests.NuGet
{
    public static class TestHelpers
    {
        public static string GenerateTempFolder()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        public static void CleanupTempFolder(ref string tempDirectory)
        {
            if (tempDirectory == null || !Directory.Exists(tempDirectory)) return;
            Directory.Delete(tempDirectory, true);
            tempDirectory = null;
        }

        public static void DoRepackForCmd(params string[] args)
        {
            DoRepackForCmd((IEnumerable<string>)args);
        }

        public static void DoRepackForCmd(IEnumerable<string> args)
        {
            var repack = new ILRepacking.ILRepack(new RepackOptions(args.Concat(new[] { "/log" })));
            repack.Repack();
        }

        public static void SaveAs(Stream input, string directory, string fileName)
        {
            var path = Path.Combine(directory, Path.GetFileName(fileName));
            using (var stream = input)
            using (var file = new FileStream(path, FileMode.Create))
            {
                stream.CopyTo(file);
            }
        }
    }
}
