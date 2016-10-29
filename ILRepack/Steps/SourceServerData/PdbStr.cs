using System.Diagnostics;
using System.IO;

namespace ILRepacking.Steps.SourceServerData
{
    internal class PdbStr
    {
        private readonly string _pdbStrPath = Path.GetTempFileName();

        public PdbStr()
        {
            using (var resourceStream = typeof(PdbStr).Assembly.GetManifestResourceStream("ILRepacking.pdbstr.exe"))
            using (var fileStream = File.Create(_pdbStrPath))
            {
                resourceStream.CopyTo(fileStream);
            }
        }

        public string Read(string pdb)
        {
            return Execute($"-r -p:{pdb} -s:srcsrv");
        }

        public void Write(string pdb, string srcsrv)
        {
            var srcsrvFile = Path.GetTempFileName();
            File.WriteAllText(srcsrvFile, srcsrv);
            Execute($"-w -p:{pdb} -s:srcsrv -i:{srcsrvFile}");
            File.Delete(srcsrvFile);
        }

        private string Execute(string arguments)
        {
            var processInfo = new ProcessStartInfo
                              {
                                  RedirectStandardOutput = true,
                                  CreateNoWindow = true,
                                  UseShellExecute = false,
                                  FileName = _pdbStrPath,
                                  Arguments = arguments
                              };
            using (var process = Process.Start(processInfo))
            using (StreamReader reader = process.StandardOutput)
            {
                return reader.ReadToEnd();
            }
        }

        ~PdbStr()
        {
            File.Delete(_pdbStrPath);
        }
    }
}
