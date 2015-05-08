using ILRepacking;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ILRepack.Tests.NuGet
{
    public static class TestHelpers
    {
        public static readonly ILogger logger = new RepackLogger();

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
            var repack = new ILRepacking.ILRepack(GetOptionsForCmd(args), TestHelpers.logger);
            repack.Repack();
        }

        public static RepackOptions GetOptionsForCmd(IEnumerable<string> args)
        {
            ICommandLine commandLine = new CommandLine(args.Concat(new []{"/log"}).ToArray());
            RepackOptions options = new RepackOptions(commandLine, TestHelpers.logger, new FileWrapper());
            options.Parse();
            return options;
        }

        public static void SaveAs(Stream input, string directory, string fileName)
        {
            var path = Path.Combine(directory, Path.GetFileName(fileName));
            using (var stream = input) {
                using (var file = new FileStream(path, FileMode.Create)) {
                    stream.CopyTo(file);
                }
            }
        }
    }
}
